namespace FreezeManager.Domain.Audit;

/// <summary>Verifies that an audit log has not been edited, reordered or truncated.</summary>
public static class AuditChain
{
    /// <summary>
    /// Walks the log in sequence order and checks four things: sequences run from 1 with no gaps,
    /// the first entry links to the genesis hash, each entry links to the one before it, and each
    /// entry's own hash still matches its contents.
    /// </summary>
    public static AuditChainVerification Verify(IEnumerable<AuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries.OrderBy(e => e.Sequence).ToArray();

        if (ordered.Length == 0)
        {
            return AuditChainVerification.Valid(0);
        }

        var expectedPrevious = AuditEntry.GenesisHash;

        for (var i = 0; i < ordered.Length; i++)
        {
            var entry = ordered[i];
            var expectedSequence = i + 1;

            if (entry.Sequence != expectedSequence)
            {
                return AuditChainVerification.Broken(
                    ordered.Length,
                    entry.Sequence,
                    $"Expected sequence {expectedSequence} but found {entry.Sequence}: an entry has been removed or reordered.");
            }

            if (!FixedTimeEquals(entry.PreviousHash, expectedPrevious))
            {
                return AuditChainVerification.Broken(
                    ordered.Length,
                    entry.Sequence,
                    i == 0
                        ? "The first entry does not link to the genesis hash."
                        : $"Entry {entry.Sequence} does not link to entry {entry.Sequence - 1}.");
            }

            var recomputed = AuditEntry.ComputeHash(
                entry.Sequence, entry.OccurredAtUtc, entry.Actor, entry.Action,
                entry.Subject, entry.Details, entry.PreviousHash);

            if (!FixedTimeEquals(entry.Hash, recomputed))
            {
                return AuditChainVerification.Broken(
                    ordered.Length,
                    entry.Sequence,
                    $"Entry {entry.Sequence} has been altered: its contents no longer match its hash.");
            }

            expectedPrevious = entry.Hash;
        }

        return AuditChainVerification.Valid(ordered.Length);
    }

    private static bool FixedTimeEquals(string left, string right) =>
        left.Length == right.Length
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(left),
            System.Text.Encoding.UTF8.GetBytes(right));
}

public sealed class AuditChainVerification
{
    private AuditChainVerification(bool isValid, int entryCount, long? firstBrokenSequence, string? reason)
    {
        IsValid = isValid;
        EntryCount = entryCount;
        FirstBrokenSequence = firstBrokenSequence;
        Reason = reason;
    }

    public bool IsValid { get; }

    public int EntryCount { get; }

    /// <summary>Where the chain first fails. Everything before it is still trustworthy.</summary>
    public long? FirstBrokenSequence { get; }

    public string? Reason { get; }

    public static AuditChainVerification Valid(int entryCount) => new(true, entryCount, null, null);

    public static AuditChainVerification Broken(int entryCount, long sequence, string reason) =>
        new(false, entryCount, sequence, reason);

    public override string ToString() => IsValid
        ? $"Chain intact across {EntryCount} entries."
        : $"Chain broken at entry {FirstBrokenSequence}: {Reason}";
}
