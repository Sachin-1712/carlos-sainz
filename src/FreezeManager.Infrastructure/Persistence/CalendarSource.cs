namespace FreezeManager.Infrastructure.Persistence;

/// <summary>Where a calendar record came from. Provenance travels with the row.</summary>
public enum CalendarSource
{
    /// <summary>The bundled seed file. Dates in it are unverified unless a person has marked them otherwise.</summary>
    Seed = 0,

    /// <summary>A live sync from the upstream calendar API.</summary>
    LiveUpstream = 1,

    /// <summary>Edited by an administrator. Survives later syncs when pinned.</summary>
    AdminEdited = 2
}
