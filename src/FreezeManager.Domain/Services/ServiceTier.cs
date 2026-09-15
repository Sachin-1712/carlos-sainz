namespace FreezeManager.Domain.Services;

/// <summary>
/// How exposed a service is to the race weekend. Freeze scope is tiered rather than global:
/// marking every service business-critical is how a freeze process loses credibility and starts
/// getting worked around.
/// </summary>
public enum ServiceTier
{
    /// <summary>
    /// Garage network, telemetry ingest, pit wall comms, timing feed. The kit is in a shipping
    /// crate or bolted to a garage wall, so the freeze starts at load-in, not at lights out.
    /// </summary>
    Trackside = 1,

    /// <summary>
    /// Mission control, strategy tooling, driver-in-loop simulator, the data pipeline. In active
    /// use throughout every session, often from the factory in parallel with the circuit.
    /// </summary>
    RaceSupport = 2,

    /// <summary>
    /// HR, finance, email, intranet. Genuinely low risk during a race weekend: advisory only.
    /// </summary>
    Corporate = 3
}
