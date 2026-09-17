namespace FreezeManager.Domain.Changes;

/// <summary>An illegal move in the change lifecycle.</summary>
public sealed class InvalidChangeTransitionException : InvalidOperationException
{
    public InvalidChangeTransitionException(ChangeState from, ChangeState to)
        : base($"A change cannot move from {from} to {to}.")
    {
        From = from;
        To = to;
    }

    public ChangeState From { get; }

    public ChangeState To { get; }
}

/// <summary>A change is not complete enough for what was asked of it.</summary>
public sealed class ChangeValidationException : InvalidOperationException
{
    public ChangeValidationException(IEnumerable<string> problems)
        : base(string.Join(" ", problems))
    {
        Problems = problems.ToArray();
    }

    public IReadOnlyList<string> Problems { get; }
}
