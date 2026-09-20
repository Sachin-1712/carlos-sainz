namespace FreezeManager.Domain.Audit;

/// <summary>What happened. One value per thing worth being able to ask about later.</summary>
public enum AuditAction
{
    ChangeRaised = 0,
    ChangeUpdated = 1,
    ChangeDeleted = 2,
    ChangeSubmitted = 3,

    /// <summary>A submission or scheduling attempt the freeze gate refused. Recorded, not discarded.</summary>
    ChangeBlockedByFreeze = 4,

    ApprovalRecorded = 5,
    ChangeApproved = 6,
    ChangeRejected = 7,
    ChangeScheduled = 8,
    ChangeWithdrawn = 9,
    ChangeCancelled = 10,
    ImplementationStarted = 11,
    ImplementationCompleted = 12,
    ImplementationFailed = 13,
    ChangeRolledBack = 14,
    ChangeClosed = 15,

    OverrideRequested = 16,
    OverrideApprovalRecorded = 17,
    OverrideGranted = 18,
    OverrideUsed = 19,

    /// <summary>A grant reached its expiry without being revoked. Written by the sweeper, not a person.</summary>
    OverrideExpired = 20,

    OverrideRevoked = 21,
    BreakGlassInvoked = 22,
    RetrospectiveCompleted = 23,
    RetrospectiveOverdue = 24
}
