using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FreezeManager.Domain.Audit;

/// <summary>
/// One immutable record of something that happened, linked to the entry before it by a hash.
/// </summary>
/// <remarks>
/// Each entry's hash covers its own contents <i>and</i> the previous entry's hash, so the log is
/// tamper-evident: editing any field, or removing or reordering any entry, breaks every hash from
/// that point on. This does not make the log tamper-proof -- someone with write access could
/// recompute the whole chain -- it makes tampering detectable, which is what an auditor asks for.
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>The previous hash of the first entry. Sixty-four zeros.</summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public AuditEntry(
        long sequence,
        DateTimeOffset occurredAtUtc,
        string actor,
        AuditAction action,
        string subject,
        string details,
        string previousHash)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousHash);
        ArgumentNullException.ThrowIfNull(details);

        Sequence = sequence;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Actor = actor;
        Action = action;
        Subject = subject;
        Details = details;
        PreviousHash = previousHash;
        Hash = ComputeHash(sequence, OccurredAtUtc, actor, action, subject, details, previousHash);
    }

    public long Sequence { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>Who did it. A person, or the name of the process for machine-written entries.</summary>
    public string Actor { get; }

    public AuditAction Action { get; }

    /// <summary>What it happened to, e.g. a change reference.</summary>
    public string Subject { get; }

    /// <summary>Free-form detail, held as JSON. Part of the hash, so it cannot be edited quietly.</summary>
    public string Details { get; }

    public string PreviousHash { get; }

    public string Hash { get; }

    /// <summary>
    /// The hash of an entry.
    /// </summary>
    /// <remarks>
    /// Fields are length-prefixed rather than joined with a separator. With a separator, an actor
    /// called <c>"a|b"</c> and a subject <c>"c"</c> would hash identically to an actor <c>"a"</c>
    /// and a subject <c>"b|c"</c> -- a forgery that needs no key. Length prefixes make the field
    /// boundaries unambiguous.
    /// </remarks>
    public static string ComputeHash(
        long sequence,
        DateTimeOffset occurredAtUtc,
        string actor,
        AuditAction action,
        string subject,
        string details,
        string previousHash)
    {
        var builder = new StringBuilder();

        Append(builder, sequence.ToString(CultureInfo.InvariantCulture));
        Append(builder, occurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        Append(builder, actor);
        Append(builder, action.ToString());
        Append(builder, subject);
        Append(builder, details);
        Append(builder, previousHash);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));

        static void Append(StringBuilder target, string value)
        {
            target.Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append(';');
        }
    }

    public override string ToString() =>
        $"#{Sequence} {OccurredAtUtc:yyyy-MM-dd HH:mm:ss}Z {Actor} {Action} {Subject}";
}
