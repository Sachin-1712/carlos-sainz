namespace FreezeManager.Domain.Freeze;

/// <summary>
/// A point in a race weekend that a freeze policy can hang an offset from.
/// </summary>
/// <remarks>
/// Policies are expressed as anchors plus offsets rather than absolute dates, so a policy written
/// once ("freeze from 48 hours before the first session until two hours after parc ferme release")
/// applies to every round for the rest of time, and follows a session when it moves.
/// </remarks>
public enum FreezeAnchor
{
    /// <summary>Start of the earliest session of the weekend. Usually FP1.</summary>
    FirstSessionStart = 0,

    /// <summary>End of the latest session of the weekend.</summary>
    LastSessionEnd = 1,

    RaceStart = 2,

    RaceEnd = 3,

    /// <summary>Start of the first parc ferme window of the weekend.</summary>
    ParcFermeStart = 4,

    /// <summary>End of the last parc ferme window -- the cars are released.</summary>
    ParcFermeRelease = 5
}
