using Xunit;

namespace FreezeManager.Infrastructure.Tests.Support;

/// <summary>
/// A fact that calls a live external service. Skipped unless <c>FREEZE_LIVE_TESTS=1</c>, so CI and
/// offline runs stay deterministic and a developer can opt in from a machine that can reach the API.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "FREEZE_LIVE_TESTS";

    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
        {
            Skip = $"Set {EnvironmentVariable}=1 to run tests that call the live calendar API.";
        }
    }
}
