using FreezeManager.Domain.Approvals;

namespace FreezeManager.Domain.Changes;

/// <summary>A request to change one or more services, and its position in the lifecycle.</summary>
public sealed class ChangeRequest
{
    private readonly List<string> _affectedServiceKeys;
    private readonly List<ChangeApproval> _approvals = new();

    public ChangeRequest(
        ChangeReference reference,
        string title,
        string requestedBy,
        ChangeType type,
        ChangeImpact impact,
        ChangeLikelihood likelihood,
        IEnumerable<string> affectedServiceKeys,
        DateTimeOffset requestedStartUtc,
        DateTimeOffset requestedEndUtc,
        DateTimeOffset createdAtUtc,
        string? description = null,
        string? implementationPlan = null,
        string? backoutPlan = null,
        ChangeState state = ChangeState.Draft,
        DateTimeOffset? updatedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentNullException.ThrowIfNull(affectedServiceKeys);

        if (requestedEndUtc <= requestedStartUtc)
        {
            throw new ArgumentException(
                "A change window must end after it starts.", nameof(requestedEndUtc));
        }

        Reference = reference;
        Title = title.Trim();
        RequestedBy = requestedBy.Trim();
        Type = type;
        Impact = impact;
        Likelihood = likelihood;
        _affectedServiceKeys = affectedServiceKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        RequestedStartUtc = requestedStartUtc.ToUniversalTime();
        RequestedEndUtc = requestedEndUtc.ToUniversalTime();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = (updatedAtUtc ?? createdAtUtc).ToUniversalTime();
        Description = description?.Trim();
        ImplementationPlan = implementationPlan?.Trim();
        BackoutPlan = backoutPlan?.Trim();
        State = state;
    }

    public ChangeReference Reference { get; }

    public string Title { get; private set; }

    public string RequestedBy { get; }

    public string? Description { get; private set; }

    public string? ImplementationPlan { get; private set; }

    public string? BackoutPlan { get; private set; }

    public ChangeType Type { get; private set; }

    public ChangeImpact Impact { get; private set; }

    public ChangeLikelihood Likelihood { get; private set; }

    /// <summary>Derived from impact and likelihood, never stored.</summary>
    public RiskLevel Risk => ChangeRiskMatrix.Evaluate(Impact, Likelihood);

    public IReadOnlyList<string> AffectedServiceKeys => _affectedServiceKeys;

    public DateTimeOffset RequestedStartUtc { get; private set; }

    public DateTimeOffset RequestedEndUtc { get; private set; }

    public TimeSpan RequestedDuration => RequestedEndUtc - RequestedStartUtc;

    public ChangeState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Only a draft can be edited or deleted. Everything else has been handed in.</summary>
    public bool IsEditable => State == ChangeState.Draft;

    public bool IsTerminal => ChangeStateMachine.IsTerminal(State);

    public IReadOnlyList<ChangeState> NextStates => ChangeStateMachine.NextStatesFrom(State);

    /// <summary>Decisions recorded against this change, in the order they were made.</summary>
    public IReadOnlyList<ChangeApproval> Approvals => _approvals;

    /// <summary>Restores approvals from storage without replaying them.</summary>
    public void RehydrateApprovals(IEnumerable<ChangeApproval> approvals)
    {
        ArgumentNullException.ThrowIfNull(approvals);

        _approvals.Clear();
        _approvals.AddRange(approvals.OrderBy(a => a.DecidedAtUtc));
    }

    /// <summary>
    /// Records one role's decision. Approvals are cleared whenever the change returns to draft, so
    /// an edited change cannot inherit approval of what it used to say.
    /// </summary>
    public void RecordApproval(ChangeApproval approval, ApprovalRequirement requirement, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(requirement);

        if (State != ChangeState.Submitted)
        {
            throw new ChangeValidationException(
                new[] { $"A change in state {State} is not awaiting approval." });
        }

        if (!requirement.Roles.Contains(approval.Role))
        {
            throw new ChangeValidationException(
                new[]
                {
                    requirement.IsPreApproved
                        ? "This change is pre-approved and takes no approvals."
                        : $"{approval.Role} is not on the approval chain for this change ({string.Join(", ", requirement.Roles)})."
                });
        }

        if (_approvals.Any(a => a.Role == approval.Role))
        {
            throw new ChangeValidationException(
                new[] { $"{approval.Role} has already recorded a decision on this change." });
        }

        _approvals.Add(approval);
        Touch(now);
    }

    public void UpdateDetails(
        string title,
        string? description,
        string? implementationPlan,
        string? backoutPlan,
        ChangeType type,
        ChangeImpact impact,
        ChangeLikelihood likelihood,
        IEnumerable<string> affectedServiceKeys,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(affectedServiceKeys);
        EnsureEditable();

        Title = title.Trim();
        Description = description?.Trim();
        ImplementationPlan = implementationPlan?.Trim();
        BackoutPlan = backoutPlan?.Trim();
        Type = type;
        Impact = impact;
        Likelihood = likelihood;

        _affectedServiceKeys.Clear();
        _affectedServiceKeys.AddRange(affectedServiceKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase));

        Touch(now);
    }

    /// <summary>
    /// Moves the window.
    /// </summary>
    /// <remarks>
    /// Allowed up to and including <see cref="ChangeState.Scheduled"/>. Approved is on the list
    /// because confirming a window is what the scheduling step does, and because approval agrees to
    /// the work rather than to the slot -- the slot is re-checked by the freeze gate either way.
    /// Rescheduling a change that is already being implemented is not a reschedule; it is a new
    /// change.
    /// </remarks>
    public void Reschedule(DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException("A change window must end after it starts.", nameof(endUtc));
        }

        if (State is not (ChangeState.Draft or ChangeState.Submitted or ChangeState.Approved or ChangeState.Scheduled))
        {
            throw new ChangeValidationException(
                new[] { $"A change in state {State} cannot be rescheduled." });
        }

        RequestedStartUtc = startUtc.ToUniversalTime();
        RequestedEndUtc = endUtc.ToUniversalTime();
        Touch(now);
    }

    public void TransitionTo(ChangeState target, DateTimeOffset now)
    {
        ChangeStateMachine.EnsureCanTransition(State, target);

        if (target == ChangeState.Submitted && State == ChangeState.Draft)
        {
            EnsureReadyForSubmission();
        }

        // Approval is of a specific change. Going back to draft to edit it discards that, so
        // nobody can approve one thing and have it apply to another.
        if (target == ChangeState.Draft)
        {
            _approvals.Clear();
        }

        State = target;
        Touch(now);
    }

    /// <summary>
    /// A draft may be incomplete; a submission may not. Requiring the backout plan here rather than
    /// at creation is what lets someone start writing a change without having solved it yet.
    /// </summary>
    public void EnsureReadyForSubmission()
    {
        var problems = new List<string>();

        if (_affectedServiceKeys.Count == 0)
        {
            problems.Add("At least one affected service is required.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            problems.Add("A description is required.");
        }

        if (string.IsNullOrWhiteSpace(ImplementationPlan))
        {
            problems.Add("An implementation plan is required.");
        }

        if (string.IsNullOrWhiteSpace(BackoutPlan))
        {
            problems.Add("A backout plan is required.");
        }

        if (problems.Count > 0)
        {
            throw new ChangeValidationException(problems);
        }
    }

    private void EnsureEditable()
    {
        if (!IsEditable)
        {
            throw new ChangeValidationException(
                new[] { $"A change in state {State} cannot be edited; withdraw it to draft first." });
        }
    }

    private void Touch(DateTimeOffset now) => UpdatedAtUtc = now.ToUniversalTime();

    public override string ToString() => $"{Reference} [{State}] {Title}";
}
