namespace FreezeManager.Domain.Approvals;

/// <summary>One person's decision, in one role.</summary>
public sealed class ChangeApproval
{
    public ChangeApproval(
        ApprovalRole role,
        string approver,
        ApprovalDecision decision,
        DateTimeOffset decidedAtUtc,
        string? comment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approver);

        Role = role;
        Approver = approver.Trim();
        Decision = decision;
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    public ApprovalRole Role { get; }

    public string Approver { get; }

    public ApprovalDecision Decision { get; }

    public DateTimeOffset DecidedAtUtc { get; }

    public string? Comment { get; }

    public bool IsApproval => Decision == ApprovalDecision.Approved;

    public override string ToString() => $"{Role} {Decision} by {Approver}";
}
