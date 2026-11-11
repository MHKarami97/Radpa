namespace Radpa.Domain.ValueObjects;

/// <summary>
/// Transparent breakdown of an application's health score (0 to 100, higher is lighter/healthier).
/// </summary>
public sealed record AppHealthScore(
    double TotalScore,
    double ResourceScore,
    double NetworkScore,
    double StartupImpactScore,
    string ExplanationKey)
{
    public string Grade => TotalScore switch
    {
        >= 85 => "A",
        >= 70 => "B",
        >= 55 => "C",
        >= 40 => "D",
        _ => "F",
    };
}
