using FreezeManager.Domain.Approvals;
using FreezeManager.Domain.Audit;
using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Overrides;
using FreezeManager.Infrastructure.Audit;
using FreezeManager.Infrastructure.Overrides;
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
    private readonly AuditWriter _audit;
    private readonly OverrideService _overrides;
    private readonly TimeProvider _time;
    private readonly int _season;

    public ChangeRequestService(
        FreezeDbContext db,
        FreezeContextFactory contextFactory,
        AuditWriter audit,
        OverrideService overrides,
        int season,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(overrides);

        _db = db;
        _contextFactory = contextFactory;
        _audit = audit;
        _overrides = overrides;
        _season = season;
        _time = time ?? TimeProvider.System;
    }

    // ------------------------------------------------------------------ reads

    public async Task<ChangeRequest?> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(reference, track: false, cancellationToken);
        return record?.ToDomainWithApprovals();
    }

    public async Task<IReadOnlyList<ChangeRequest>> ListAsync(
        ChangeState? state = null,
        string? affectedServiceKey = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ChangeRequests.AsNoTracking()
            .Include(c => c.AffectedServices)
            .Include(c => c.Approvals)
            .AsQueryable();

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

        return records.Select(r => r.ToDomainWithApprovals()).ToArray();
    }

    /// <summary>The approval chain a change needs, given the tiers it touches and how it was raised.</summary>
    public async Task<ApprovalRequirement> RequirementForAsync(ChangeRequest change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var context = await _contextFactory.CreateAsync(_season, cancellationToken);

        var tiers = change.AffectedServiceKeys
            .Where(context.TiersByServiceKey.ContainsKey)
            .Select(k => context.TiersByServiceKey[k])
            .ToArray();

        return ApprovalRequirement.For(tiers, change.Type);
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

        await _audit.AppendAsync(
            change.RequestedBy, AuditAction.ChangeRaised, change.Reference.ToString(),
            new { change.Title, type = change.Type.ToString(), risk = change.Risk.ToString(), services = change.AffectedServiceKeys },
            cancellationToken);

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

        var change = record.ToDomainWithApprovals();

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

        await _audit.AppendAsync(
            change.RequestedBy, AuditAction.ChangeUpdated, change.Reference.ToString(),
            new { change.Title, window = new { change.RequestedStartUtc, change.RequestedEndUtc } },
            cancellationToken);

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

        var change = record.ToDomainWithApprovals();

        if (!change.IsEditable)
        {
            return ChangeOperationResult.Invalid(
                change,
                new[] { $"A change in state {change.State} cannot be deleted; cancel it instead." });
        }

        _db.ChangeRequests.Remove(record);

        // The change is gone; the record that it existed and was deleted is not.
        await _audit.AppendAsync(
            change.RequestedBy, AuditAction.ChangeDeleted, change.Reference.ToString(),
            new { change.Title }, cancellationToken);

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

        var change = record.ToDomainWithApprovals();

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

        // Moving to Approved is not something a caller asserts; it is something the approval chain
        // earns. Checking it here rather than in the endpoint means no route can bypass it.
        if (target == ChangeState.Approved)
        {
            var requirement = await RequirementForAsync(change, cancellationToken);

            if (ApprovalRequirement.IsRejected(change.Approvals))
            {
                return ChangeOperationResult.Invalid(
                    change, new[] { "An approver rejected this change. Reject it or return it to draft." });
            }

            var outstanding = requirement.OutstandingRoles(change.Approvals);

            if (outstanding.Count > 0)
            {
                return ChangeOperationResult.Invalid(
                    change,
                    new[] { $"The approval chain is not complete. Still required: {string.Join(", ", outstanding)}." });
            }
        }

        FreezeGateDecision? decision = null;
        FreezeOverride? usedOverride = null;

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
                // A blocked change may still proceed on a granted, unexpired override. This is the
                // only way through a freeze, and spending it is recorded.
                usedOverride = await _overrides.InForceAsync(change.Reference.ToString(), cancellationToken);

                if (usedOverride is null)
                {
                    await _audit.AppendAsync(
                        change.RequestedBy,
                        AuditAction.ChangeBlockedByFreeze,
                        change.Reference.ToString(),
                        new
                        {
                            requestedState = target.ToString(),
                            window = new { change.RequestedStartUtc, change.RequestedEndUtc },
                            conflicts = decision.Conflicts.Select(c => c.Reason).ToArray(),
                            suggestedOpenWindowStartUtc = decision.SuggestedOpenWindow?.StartUtc
                        },
                        cancellationToken);

                    await _db.SaveChangesAsync(cancellationToken);

                    return ChangeOperationResult.Blocked(change, decision);
                }
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

        if (usedOverride is not null)
        {
            await MarkOverrideUsedAsync(change, usedOverride, now, cancellationToken);
        }

        await _audit.AppendAsync(
            change.RequestedBy,
            AuditActionFor(target),
            change.Reference.ToString(),
            new
            {
                state = target.ToString(),
                window = new { change.RequestedStartUtc, change.RequestedEndUtc },
                viaOverride = usedOverride is not null,
                incidentReference = usedOverride?.IncidentReference
            },
            cancellationToken);

        // A change that clears its chain the moment it is submitted -- a pre-approved standard
        // change -- should not sit waiting for an approval nobody owes it.
        if (target == ChangeState.Submitted)
        {
            var requirement = await RequirementForAsync(change, cancellationToken);

            if (requirement.IsPreApproved)
            {
                change.TransitionTo(ChangeState.Approved, now);

                await _audit.AppendAsync(
                    "system:standard-change",
                    AuditAction.ChangeApproved,
                    change.Reference.ToString(),
                    new { rationale = requirement.Rationale },
                    cancellationToken);
            }
        }

        record.ApplyFrom(change);
        await _db.SaveChangesAsync(cancellationToken);

        return ChangeOperationResult.Ok(change, decision, usedOverride);
    }

    /// <summary>Records one role's decision, and completes the chain when it is the last one.</summary>
    public async Task<ChangeOperationResult> RecordApprovalAsync(
        string reference,
        ApprovalRole role,
        string approver,
        ApprovalDecision decision,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(reference, track: true, cancellationToken);

        if (record is null)
        {
            return ChangeOperationResult.NotFound(reference);
        }

        var change = record.ToDomainWithApprovals();
        var requirement = await RequirementForAsync(change, cancellationToken);
        var now = _time.GetUtcNow();

        try
        {
            change.RecordApproval(new ChangeApproval(role, approver, decision, now, comment), requirement, now);
        }
        catch (ChangeValidationException exception)
        {
            return ChangeOperationResult.Invalid(change, exception.Problems);
        }

        await _audit.AppendAsync(
            approver,
            AuditAction.ApprovalRecorded,
            change.Reference.ToString(),
            new { role = role.ToString(), decision = decision.ToString(), comment },
            cancellationToken);

        if (decision == ApprovalDecision.Rejected)
        {
            change.TransitionTo(ChangeState.Rejected, now);

            await _audit.AppendAsync(
                approver, AuditAction.ChangeRejected, change.Reference.ToString(),
                new { role = role.ToString(), comment }, cancellationToken);
        }
        else if (requirement.IsSatisfiedBy(change.Approvals))
        {
            change.TransitionTo(ChangeState.Approved, now);

            await _audit.AppendAsync(
                approver, AuditAction.ChangeApproved, change.Reference.ToString(),
                new { chain = requirement.Roles.Select(r => r.ToString()).ToArray() }, cancellationToken);
        }

        record.ApplyFrom(change);
        await _db.SaveChangesAsync(cancellationToken);

        return ChangeOperationResult.Ok(change);
    }

    private async Task MarkOverrideUsedAsync(
        ChangeRequest change,
        FreezeOverride granted,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var overrideRecord = await _db.FreezeOverrides
            .Include(o => o.Approvals)
            .Where(o => o.ChangeReference == change.Reference.ToString() && o.State == OverrideState.Granted)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (overrideRecord is null)
        {
            return;
        }

        var request = overrideRecord.ToDomain();
        request.MarkUsed(now);
        overrideRecord.ApplyFrom(request);

        await _audit.AppendAsync(
            change.RequestedBy,
            AuditAction.OverrideUsed,
            change.Reference.ToString(),
            new
            {
                incidentReference = request.IncidentReference,
                breakGlass = request.IsBreakGlass,
                grantedAtUtc = request.GrantedAtUtc,
                expiresAtUtc = request.ExpiresAtUtc
            },
            cancellationToken);
    }

    private static AuditAction AuditActionFor(ChangeState state) => state switch
    {
        ChangeState.Submitted => AuditAction.ChangeSubmitted,
        ChangeState.Approved => AuditAction.ChangeApproved,
        ChangeState.Rejected => AuditAction.ChangeRejected,
        ChangeState.Scheduled => AuditAction.ChangeScheduled,
        ChangeState.Draft => AuditAction.ChangeWithdrawn,
        ChangeState.Cancelled => AuditAction.ChangeCancelled,
        ChangeState.Implementing => AuditAction.ImplementationStarted,
        ChangeState.Implemented => AuditAction.ImplementationCompleted,
        ChangeState.Failed => AuditAction.ImplementationFailed,
        ChangeState.RolledBack => AuditAction.ChangeRolledBack,
        ChangeState.Closed => AuditAction.ChangeClosed,
        _ => AuditAction.ChangeUpdated
    };

    // ------------------------------------------------------------------ helpers

    private Task<ChangeRequestRecord?> FindAsync(string reference, bool track, CancellationToken cancellationToken)
    {
        var query = _db.ChangeRequests
            .Include(c => c.AffectedServices)
            .Include(c => c.Approvals)
            .AsQueryable();

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
