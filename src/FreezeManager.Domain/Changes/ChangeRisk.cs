namespace FreezeManager.Domain.Changes;

public enum ChangeImpact
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum ChangeLikelihood
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Impact against likelihood, the standard ITSM risk matrix. Derived, never stored: two people
/// cannot then disagree about the risk of the same assessment.
/// </summary>
public static class ChangeRiskMatrix
{
    public static RiskLevel Evaluate(ChangeImpact impact, ChangeLikelihood likelihood) =>
        (impact, likelihood) switch
        {
            (ChangeImpact.Low, ChangeLikelihood.Low) => RiskLevel.Low,
            (ChangeImpact.Low, ChangeLikelihood.Medium) => RiskLevel.Low,
            (ChangeImpact.Low, ChangeLikelihood.High) => RiskLevel.Medium,

            (ChangeImpact.Medium, ChangeLikelihood.Low) => RiskLevel.Low,
            (ChangeImpact.Medium, ChangeLikelihood.Medium) => RiskLevel.Medium,
            (ChangeImpact.Medium, ChangeLikelihood.High) => RiskLevel.High,

            (ChangeImpact.High, ChangeLikelihood.Low) => RiskLevel.Medium,
            (ChangeImpact.High, ChangeLikelihood.Medium) => RiskLevel.High,
            (ChangeImpact.High, ChangeLikelihood.High) => RiskLevel.Critical,

            _ => RiskLevel.Medium
        };
}
