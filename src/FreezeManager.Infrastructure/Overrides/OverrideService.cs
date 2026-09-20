using FreezeManager.Domain.Approvals;
using FreezeManager.Domain.Audit;
using FreezeManager.Domain.Changes;
using FreezeManager.Domain.Overrides;
using FreezeManager.Infrastructure.Audit;
using FreezeManager.Infrastructure.Changes;
using FreezeManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.Overrides;

public enum OverrideOperationStatus
{
    Succeeded = 0,
    NotFound = 1,
    Invalid = 2,

    /// <summary>The change is not in a state where an override means anything.</summary>
    NotApplicable = 3
}

public sealed class OverrideOperationResult
{
    private OverrideOperationResult(
        OverrideOperationStatus status,
        FreezeOverride? freezeOverride,
        IReadOnlyList<ApprovalRole> outstandingRoles,
        IReadOnlyList<string> problems)
    {
        Status = status;
        Override = freezeOverride;
        OutstandingRoles = outstandingRoles;
        Problems = problems;
    }

    public OverrideOperationStatus Status { get; }

    public FreezeOverride? Override { get; }

    /// <summary>Roles still to decide. Empty once the override is granted or rejected.</summary>
    public IReadOnlyList<ApprovalRole> OutstandingRoles { get; }

    public IReadOnlyList<string> Problems { get; }

    public bool Succeeded => Status == OverrideOperationStatus.Succeeded;

    public static OverrideOperationResult Ok(FreezeOverride source, IReadOnlyList<ApprovalRole> outstanding) =>
        new(OverrideOperationStatus.Succeeded, source, outstanding, Array.Empty<string>());

    public static OverrideOperationResult NotFound(string what) =>
        new(OverrideOperationStatus.NotFound, null, Array.Empty<ApprovalRole>(), new[] { what });

    public static OverrideOperationResult Invalid(IEnumerable<string> problems) =>
        new(OverrideOperationStatus.Invalid, null, Array.Empty<ApprovalRole>(), problems.ToArray());

    public static OverrideOperationResult NotApplicable(string reason) =>
        new(OverrideOperationStatus.NotApplicable, null, Array.Empty<ApprovalRole>(), new[] { reason });
}

/// <summary>
/// Requests, approves and spends emergency overrides, writing an audit entry at every step.
/// </summary>
public sealed class OverrideService
{
    private readonly FreezeDbContext _db;
    private readonly FreezeContextFactory _contextFactory;
    private readonly AuditWriter _audit;
    private readonly TimeProvider _time;
    private readonly int _season;

    public OverrideService(
        FreezeDbContext db,
        FreezeContextFactory contextFactory,
        AuditWriter audit,
        int season,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(audit);

        _db = db;
        _contextFactory = contextFactory;
        _audit = audit;
        _season = season;
        _time = time ?? TimeProvider.System;
    }

    public async Task<OverrideOperationResult> RequestAsync(
        string changeReference,
        string incidentReference,
        string justification,
        string requestedBy,
        TimeSpan? grantDuration,
        bool breakGlass,
        CancellationToken cancellationToken = default)
    {
        var changeRecord = await FindChangeAsync(changeReference, cancellationToken);

        if (changeRecord is null)
        {
            return OverrideOperationResult.NotFound($"No change with reference '{changeReference}'.");
        }

        var change = changeRecord.ToDomainWithApprovals();

        if (change.IsTerminal)
        {
            return OverrideOperationResult.NotApplicable(
                $"A change in state {change.State} cannot be overridden.");
        }

        var existing = await _db.FreezeOverrides
            .Include(o => o.Approvals)
            .Where(o => o.ChangeReference == changeReference)
            .ToListAsync(cancellationToken);

        var now = _time.GetUtcNow();

        if (existing.Any(o => o.State is OverrideState.Requested or OverrideState.Granted))
        {
            return OverrideOperationResult.Invalid(
                new[] { "This change already has an override outstanding. Revoke it before raising another." });
        }

        FreezeOverride request;

        try
        {
            request = new FreezeOverride(
                changeReference, incidentReference, justification, requestedBy, now, grantDuration, breakGlass);
        }
        catch (OverrideValidationException exception)
        {
            return OverrideOperationResult.Invalid(exception.Problems);
        }

        var record = new FreezeOverrideRecord();
        record.ApplyFrom(request);
        _db.FreezeOverrides.Add(record);

        var requirement = await RequirementForAsync(change, cancellationToken);
        var required = request.RequiredRoles(requirement);

        await _audit.AppendAsync(
            requestedBy,
            breakGlass ? AuditAction.BreakGlassInvoked : AuditAction.OverrideRequested,
            changeReference,
            new
            {
                incidentReference = request.IncidentReference,
                justification = request.Justification,
                breakGlass,
                grantDurationMinutes = request.GrantDuration.TotalMinutes,
                requiredRoles = required.Select(r => r.ToString()).ToArray()
            },
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OverrideOperationResult.Ok(request, required);
    }

    public async Task<OverrideOperationResult> ApproveAsync(
        string changeReference,
        ApprovalRole role,
        string approver,
        ApprovalDecision decision,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var record = await FindOutstandingOverrideAsync(changeReference, cancellationToken);

        if (record is null)
        {
            return OverrideOperationResult.NotFound($"No outstanding override for '{changeReference}'.");
        }

        var changeRecord = await FindChangeAsync(changeReference, cancellationToken);

        if (changeRecord is null)
        {
            return OverrideOperationResult.NotFound($"No change with reference '{changeReference}'.");
        }

        var requirement = await RequirementForAsync(changeRecord.ToDomainWithApprovals(), cancellationToken);
        var request = record.ToDomain();
        var now = _time.GetUtcNow();

        try
        {
            request.RecordApproval(new ChangeApproval(role, approver, decision, now, comment), requirement, now);
        }
        catch (OverrideValidationException exception)
        {
            return OverrideOperationResult.Invalid(exception.Problems);
        }

        record.ApplyFrom(request);

        await _audit.AppendAsync(
            approver,
            AuditAction.OverrideApprovalRecorded,
            changeReference,
            new { role = role.ToString(), decision = decision.ToString(), comment },
            cancellationToken);

        if (request.State == OverrideState.Granted)
        {
            await _audit.AppendAsync(
                approver,
                AuditAction.OverrideGranted,
                changeReference,
                new
                {
                    incidentReference = request.IncidentReference,
                    breakGlass = request.IsBreakGlass,
                    grantedAtUtc = request.GrantedAtUtc,
                    expiresAtUtc = request.ExpiresAtUtc,
                    retrospectiveDueAtUtc = request.RetrospectiveDueAtUtc
                },
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var outstanding = request.State == OverrideState.Requested
            ? request.RequiredRoles(requirement).Where(r => request.Approvals.All(a => a.Role != r)).ToArray()
            : Array.Empty<ApprovalRole>();

        return OverrideOperationResult.Ok(request, outstanding);
    }

    public async Task<OverrideOperationResult> RevokeAsync(
        string changeReference,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var record = await FindOutstandingOverrideAsync(changeReference, cancellationToken);

        if (record is null)
        {
            return OverrideOperationResult.NotFound($"No outstanding override for '{changeReference}'.");
        }

        var request = record.ToDomain();

        try
        {
            request.Revoke(_time.GetUtcNow());
        }
        catch (OverrideValidationException exception)
        {
            return OverrideOperationResult.Invalid(exception.Problems);
        }

        record.ApplyFrom(request);
        await _audit.AppendAsync(actor, AuditAction.OverrideRevoked, changeReference, null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return OverrideOperationResult.Ok(request, Array.Empty<ApprovalRole>());
    }

    public async Task<OverrideOperationResult> CompleteRetrospectiveAsync(
        string changeReference,
        string actor,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.FreezeOverrides
            .Include(o => o.Approvals)
            .Where(o => o.ChangeReference == changeReference && o.IsBreakGlass)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return OverrideOperationResult.NotFound($"No break-glass override for '{changeReference}'.");
        }

        var request = record.ToDomain();
        var now = _time.GetUtcNow();

        try
        {
            request.CompleteRetrospective(notes, now);
        }
        catch (OverrideValidationException exception)
        {
            return OverrideOperationResult.Invalid(exception.Problems);
        }

        record.ApplyFrom(request);

        await _audit.AppendAsync(
            actor,
            AuditAction.RetrospectiveCompleted,
            changeReference,
            new
            {
                incidentReference = request.IncidentReference,
                dueAtUtc = request.RetrospectiveDueAtUtc,
                completedAtUtc = request.RetrospectiveCompletedAtUtc,
                wasOverdue = request.RetrospectiveDueAtUtc is not null && now > request.RetrospectiveDueAtUtc.Value,
                notes = request.RetrospectiveNotes
            },
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OverrideOperationResult.Ok(request, Array.Empty<ApprovalRole>());
    }

    /// <summary>The override currently in force for a change, if any.</summary>
    public async Task<FreezeOverride?> InForceAsync(string changeReference, CancellationToken cancellationToken = default)
    {
        var record = await _db.FreezeOverrides
            .AsNoTracking()
            .Include(o => o.Approvals)
            .Where(o => o.ChangeReference == changeReference && o.State == OverrideState.Granted)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var request = record?.ToDomain();

        return request is not null && request.IsInForceAt(_time.GetUtcNow()) ? request : null;
    }

    public async Task<IReadOnlyList<FreezeOverride>> ListAsync(
        string? changeReference = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.FreezeOverrides.AsNoTracking().Include(o => o.Approvals).AsQueryable();

        if (!string.IsNullOrWhiteSpace(changeReference))
        {
            query = query.Where(o => o.ChangeReference == changeReference);
        }

        var records = await query.OrderByDescending(o => o.Id).ToListAsync(cancellationToken);
        return records.Select(r => r.ToDomain()).ToArray();
    }

    private Task<ChangeRequestRecord?> FindChangeAsync(string reference, CancellationToken cancellationToken) =>
        _db.ChangeRequests
            .Include(c => c.AffectedServices)
            .Include(c => c.Approvals)
            .FirstOrDefaultAsync(c => c.Reference == reference, cancellationToken);

    private Task<FreezeOverrideRecord?> FindOutstandingOverrideAsync(string reference, CancellationToken cancellationToken) =>
        _db.FreezeOverrides
            .Include(o => o.Approvals)
            .Where(o => o.ChangeReference == reference
                        && (o.State == OverrideState.Requested || o.State == OverrideState.Granted))
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<ApprovalRequirement> RequirementForAsync(ChangeRequest change, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateAsync(_season, cancellationToken);

        var tiers = change.AffectedServiceKeys
            .Where(context.TiersByServiceKey.ContainsKey)
            .Select(k => context.TiersByServiceKey[k])
            .ToArray();

        return ApprovalRequirement.For(tiers, change.Type);
    }
}
