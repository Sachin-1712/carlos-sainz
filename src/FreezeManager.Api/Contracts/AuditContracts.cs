using FreezeManager.Domain.Audit;
using FreezeManager.Infrastructure.Persistence;

namespace FreezeManager.Api.Contracts;

public sealed record AuditEntryResponse
{
    public required long Sequence { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required string Actor { get; init; }

    public required string Action { get; init; }

    public required string Subject { get; init; }

    public required string Details { get; init; }

    public required string PreviousHash { get; init; }

    public required string Hash { get; init; }

    public static AuditEntryResponse From(AuditEntryRecord record) => new()
    {
        Sequence = record.Sequence,
        OccurredAtUtc = new DateTimeOffset(DateTime.SpecifyKind(record.OccurredAtUtc, DateTimeKind.Utc)),
        Actor = record.Actor,
        Action = record.Action.ToString(),
        Subject = record.Subject,
        Details = record.Details,
        PreviousHash = record.PreviousHash,
        Hash = record.Hash
    };
}

public sealed record AuditVerificationResponse
{
    public required bool IsValid { get; init; }

    public required int EntryCount { get; init; }

    /// <summary>Where the chain first fails. Everything before it is still trustworthy.</summary>
    public long? FirstBrokenSequence { get; init; }

    public string? Reason { get; init; }

    /// <summary>What this check can and cannot tell you, so the answer is not over-read.</summary>
    public required string Scope { get; init; }

    public static AuditVerificationResponse From(AuditChainVerification verification) => new()
    {
        IsValid = verification.IsValid,
        EntryCount = verification.EntryCount,
        FirstBrokenSequence = verification.FirstBrokenSequence,
        Reason = verification.Reason,
        Scope = "Detects edited, removed or reordered entries. Entries removed from the end of the "
                + "log cannot be detected by a hash chain alone: compare EntryCount against an "
                + "independently recorded high-water mark."
    };
}
