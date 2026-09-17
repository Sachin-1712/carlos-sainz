using FreezeManager.Domain.Changes;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.Changes;

/// <summary>
/// Orchestrates the change lifecycle: load, apply a domain operation, run the freeze gate where the
/// state machine says it is required, and persist.
/// </summary>
/// <remarks>
/// Which transitions need the gate is decided by <see cref="ChangeStateMachine.RequiresFreezeCheck"/>,
/// not by this class. Keeping that in the table means a new gated transition cannot be added without
/// the gate following it.
/// </remarks>
public sealed class ChangeRequestService
{
    private readonly FreezeDbContext _db;
    private readonly FreezeContextFactory _contextFactory;
    private readonly TimeProvider _time;
    private readonly int _season;

    public ChangeRequestService(
        FreezeDbContext db,
        FreezeContextFactory contextFactory,
        int season,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(contextFactory);

        _db = db;
        _contextFactory = contextFactory;
        _season = season;
        _time = time ?? TimeProvider.System;
    }

    // ------------------------------------------------------------------ reads

    public async Task<ChangeRequest?> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(reference, track: false, cancellationToken);
        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<ChangeRequest>> ListAsync(
        ChangeState? state = null,
        string? affectedServiceKey = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ChangeRequests.AsNoTracking().Include(c => c.AffectedServices).AsQueryable();

        if (state.HasValue)
        {
            query = query.Where(c => c.State == state.Value);
        }

        if (!string.IsNullOrWhiteSpace(affectedServiceKey))
        {
            query = query.Where(c => c.AffectedServices.Any(s => s.ServiceKey == affectedServiceKey));
        }

        var records = await query
            .OrderByDescending(c => c.ReferenceYear)
            .ThenByDescending(c => c.ReferenceSequence)
            .ToListAsync(cancellationToken);

        return records.Select(r => r.ToDomain()).ToArray();
    }

    // ------------------------------------------------------------------ writes

    public async Task<ChangeOperationResult> CreateDraftAsync(
        NewChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _time.GetUtcNow();
        ChangeRequest change;

        try
        {
            change = new ChangeRequest(
                await NextReferenceAsync(now.Year, cancellationToken),
                request.Title,
                request.RequestedBy,
                request.Type,
                request.Impact,
                request.Likelihood,
                request.AffectedServiceKeys,
                request.RequestedStartUtc,
                request.RequestedEndUtc,
                now,
                request.Description,
                request.ImplementationPlan,
                request.BackoutPlan);
        }
        catch (ArgumentException exception)
        {
            return ChangeOperationResult.Invalid(null, new[] { exception.Message });
        }

        var record = new ChangeRequestRecord();
        record.ApplyFrom(change);
        _db.ChangeRequests.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        return ChangeOperationResult.Ok(change);
    }

    public async Task<ChangeOperationResult> UpdateDraftAsync(
        string reference,
        NewChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record = await FindAsync(reference, track: true, cancellationToken);

        if (record is null)
        {
            return ChangeOperationResult.NotFound(reference);
        }

        var change = record.ToDomain();

        try
        {
            change.UpdateDetails(
                request.Title,
                request.Description,
                request.ImplementationPlan,
                request.BackoutPlan,
                request.Type,
                request.Impact,
                request.Likelihood,
                request.AffectedServiceKeys,
                _time.GetUtcNow());

            change.Reschedule(request.RequestedStartUtc, request.RequestedEndUtc, _time.GetUtcNow());
        }
        catch (ChangeValidationException exception)
        {
            return ChangeOperationResult.Invalid(change, exception.Problems);
        }
        catch (ArgumentException exception)
        {
            return ChangeOperationResult.Invalid(change, new[] { exception.Message });
        }

        record.ApplyFrom(change);
        await _db.SaveChangesAsync(cancellationToken);

        return ChangeOperationResult.Ok(change);
    }

    public async Task<ChangeOperationResult> DeleteDraftAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(reference, track: true, cancellationToken);

        if (record is null)
        {
            return ChangeOperationResult.NotFound(reference);
        }

        var change = record.ToDomain();

        if (!change.IsEditable)
        {
            return ChangeOperationResult.Invalid(
                change,
                new[] { $"A change in state {change.State} cannot be deleted; cancel it instead." });
        }

        _db.ChangeRequests.Remove(record);
        await _db.SaveChangesAsync(cancellationToken);

        return ChangeOperationResult.Ok(change);
    }

    /// <summary>
    /// Moves a change to a new state, running the freeze gate first where the table requires it.
    /// </summary>
    /// <param name="newWindow">
    /// An optional replacement window, applied before the gate runs. This is what makes taking the
    /// suggested window a single call: read the window off the rejection, send it straight back.
    /// </param>
    public async Task<ChangeOperationResult> TransitionAsync(
        string reference,
        ChangeState target,
        (DateTimeOffset Start, DateTimeOffset End)? newWindow = null,
        CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(reference, track: true, cancellationToken);

        if (record is null)
        {
            return ChangeOperationResult.NotFound(reference);
        }

        var change = record.ToDomain();

        if (!ChangeStateMachine.CanTransition(change.State, target))
        {
            return ChangeOperationResult.IllegalTransition(change, target);
        }

        var now = _time.GetUtcNow();

        if (newWindow.HasValue)
        {
            try
            {
                change.Reschedule(newWindow.Value.Start, newWindow.Value.End, now);
            }
            catch (Exception exception) when (exception is ArgumentException or ChangeValidationException)
            {
                return ChangeOperationResult.Invalid(change, new[] { exception.Message });
            }
        }

        FreezeGateDecision? decision = null;

        if (ChangeStateMachine.RequiresFreezeCheck(change.State, target))
        {
            // Completeness is checked before the gate: telling someone their window clashes when
            // they have not written a backout plan yet is answering the wrong question.
            try
            {
                change.EnsureReadyForSubmission();
            }
            catch (ChangeValidationException exception)
            {
                return ChangeOperationResult.Invalid(change, exception.Problems);
            }

            var context = await _contextFactory.CreateAsync(_season, cancellationToken);
            decision = context.Gate.Evaluate(change, context.TiersByServiceKey);

            if (decision.Outcome == FreezeGateOutcome.UnknownService)
            {
                return ChangeOperationResult.Invalid(
                    change,
                    decision.UnknownServiceKeys.Select(k => $"Unknown service '{k}'."));
            }

            if (!decision.IsAllowed)
            {
                return ChangeOperationResult.Blocked(change, decision);
            }
        }

        try
        {
            change.TransitionTo(target, now);
        }
        catch (ChangeValidationException exception)
        {
            return ChangeOperationResult.Invalid(change, exception.Problems);
        }
        catch (InvalidChangeTransitionException)
        {
            return ChangeOperationResult.IllegalTransition(change, target);
        }

        record.ApplyFrom(change);
        await _db.SaveChangesAsync(cancellationToken);

        return ChangeOperationResult.Ok(change, decision);
    }

    // ------------------------------------------------------------------ helpers

    private Task<ChangeRequestRecord?> FindAsync(string reference, bool track, CancellationToken cancellationToken)
    {
        var query = _db.ChangeRequests.Include(c => c.AffectedServices).AsQueryable();

        if (!track)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(c => c.Reference == reference, cancellationToken);
    }

    private async Task<ChangeReference> NextReferenceAsync(int year, CancellationToken cancellationToken)
    {
        var highest = await _db.ChangeRequests
            .Where(c => c.ReferenceYear == year)
            .MaxAsync(c => (int?)c.ReferenceSequence, cancellationToken);

        return ChangeReference.Create(year, (highest ?? 0) + 1);
    }
}

/// <summary>The fields a caller supplies when creating or editing a change.</summary>
public sealed record NewChangeRequest
{
    public required string Title { get; init; }

    public required string RequestedBy { get; init; }

    public string? Description { get; init; }

    public string? ImplementationPlan { get; init; }

    public string? BackoutPlan { get; init; }

    public ChangeType Type { get; init; } = ChangeType.Normal;

    public ChangeImpact Impact { get; init; } = ChangeImpact.Medium;

    public ChangeLikelihood Likelihood { get; init; } = ChangeLikelihood.Medium;

    public IReadOnlyList<string> AffectedServiceKeys { get; init; } = Array.Empty<string>();

    public required DateTimeOffset RequestedStartUtc { get; init; }

    public required DateTimeOffset RequestedEndUtc { get; init; }
}
