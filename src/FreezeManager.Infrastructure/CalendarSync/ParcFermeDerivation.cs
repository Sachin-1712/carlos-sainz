using FreezeManager.Domain.Calendar;
using FreezeManager.Infrastructure.Persistence;

namespace FreezeManager.Infrastructure.CalendarSync;

/// <summary>
/// Produces default parc ferme windows from session times when the source publishes none.
/// </summary>
/// <remarks>
/// The engine reads parc ferme as data (it never derives it). This is the adapter that gets the
/// data there when the source lacks it. Every window it produces is stamped derived, so a person
/// can see which came from a rule and which from a person, and so the next sync replaces the
/// rule's windows without touching a person's.
/// </remarks>
public static class ParcFermeDerivation
{
    public static List<ParcFermeWindowRecord> Derive(IEnumerable<SessionRecord> sessions, IngestionDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(defaults);

        var byType = sessions.ToDictionary(s => s.Type);
        var windows = new List<ParcFermeWindowRecord>();

        if (byType.TryGetValue(SessionType.SprintQualifying, out var sprintQualifying)
            && byType.TryGetValue(SessionType.Sprint, out var sprint)
            && sprint.ScheduledEndUtc > sprintQualifying.ScheduledStartUtc)
        {
            windows.Add(new ParcFermeWindowRecord
            {
                StartUtc = sprintQualifying.ScheduledStartUtc,
                EndUtc = sprint.ScheduledEndUtc,
                Label = "Sprint parc ferme (derived)",
                IsDerived = true
            });
        }

        if (byType.TryGetValue(SessionType.Qualifying, out var qualifying)
            && byType.TryGetValue(SessionType.Race, out var race))
        {
            var release = race.ScheduledEndUtc + defaults.ParcFermeReleaseAfterRace;

            if (release > qualifying.ScheduledStartUtc)
            {
                windows.Add(new ParcFermeWindowRecord
                {
                    StartUtc = qualifying.ScheduledStartUtc,
                    EndUtc = release,
                    Label = "Race parc ferme (derived)",
                    IsDerived = true
                });
            }
        }

        return windows;
    }
}
