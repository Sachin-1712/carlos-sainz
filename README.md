# Race Weekend Change Freeze Manager

[![CI](https://github.com/Sachin-1712/carlos-sainz/actions/workflows/ci.yml/badge.svg?branch=claude/amazing-hamilton-jqb41y)](https://github.com/Sachin-1712/carlos-sainz/actions/workflows/ci.yml)

An IT change-management system in which **the race calendar is a first-class scheduling
constraint**.

Engineers raise change requests against IT services. The system knows when every session of every
race weekend starts and ends, derives freeze windows from that calendar according to per-service
policy, and refuses to let a change be scheduled into one. Emergencies still get through, but only
via a time-boxed override that costs a named approval chain, a linked incident, and a permanent,
tamper-evident audit entry.

> **Portfolio project.** Not affiliated with, endorsed by, or derived from any Formula 1 team's
> internal systems. All data is seeded or fetched from public sources.

## Why the calendar matters

Corporate IT change freezes are calendars of convenience: month-end, Black Friday, the Christmas
shutdown. Someone picks them, and someone can move them.

A racing team's freeze is different. It is externally imposed, non-negotiable, and it moves without
asking. The FIA publishes when you are allowed to break things. Nobody in your change process gets
a vote, and a session that slips because of weather drags every dependent freeze window with it.

That single difference is what this project is built around.

## Design decisions worth reading

**Parc fermé is stored as data, not computed in code.** The shape of parc fermé on a sprint weekend
has changed more than once as the sporting regulations have been revised. If the rule lives in an
`if` statement, a regulation change becomes a code change and a release. If it lives in the
calendar, it becomes an edit by a duty manager.

**Freeze scope is tiered, not global.** Trackside systems freeze from load-in. Race-support systems
track the sessions. Corporate IT gets an advisory warning and nothing more, because marking every
service business-critical is how a freeze process loses credibility and starts getting bypassed.

**The calendar is editable, with audit.** Sessions get moved. Qualifying gets pushed to Sunday
morning. A freeze tool that can only mirror a static API, and cannot be corrected by a duty manager
at 06:00, is a tool that gets worked around -- and a bypassed control is worse than no control.

**Everything is UTC internally, displayed in three zones.** Circuit-local, factory-local, and UTC.
The bug this exists to prevent is the week each spring and autumn when the US and Europe have not
yet both changed their clocks, and an Austin freeze window is quietly an hour wrong.

**Freeze windows are half-open intervals.** `[start, end)`. Two back-to-back windows never both
claim the boundary instant, and "frozen until 14:00" means what an engineer thinks it means.

## Status

Built in phases, one review at the end of each. Scope is phases 0-5; a full front end, continual
improvement metrics and anything AI-assisted are deliberately out of scope.

| Phase | Scope | Status |
| --- | --- | --- |
| 0 | Repository scaffold, CI | Done |
| 1 | Domain: race calendar model, freeze engine, test suite | Done |
| 2 | Persistence, calendar ingestion (live provider with seeded fallback) | Done |
| 3 | API: change request lifecycle and freeze gate | Done |
| 4 | Approval chains, emergency override, audit hash chain | Not started |
| 5 | Minimal single-page freeze dashboard | Not started |

Every design decision is written up in [`docs/decisions.md`](docs/decisions.md), one short entry
each, in the form of what was chosen, why, and what it would cost to get wrong.

## Things that are placeholders, deliberately

**Tier policy offsets are not yet set.** `FreezePolicySet.Default` freezes trackside from 48 hours
before the first session to 2 hours after parc ferme release, race support from 24 hours before to
3 hours after the race, and gives corporate a 30-minute advisory margin around each session. Those
numbers are plausible, not decided: they exist so the engine has something to run and are **to be
set by the service owner**. They are data, not code -- one edit in `FreezePolicySet.cs`.

**"Verified" means provenance, not review.** A row is `IsVerified = true` when it **came from a
published source** -- the live upstream API -- and `false` when it did not. It does **not** mean a
person has checked it. Nothing in this project asserts human review of calendar data; the flag
answers "where did this come from?", which is the question that matters when a freeze window looks
wrong at 06:00.

**The live provider is the normal source of calendar data, and is verified against the real
API.** A live sync pulls the real season -- rounds, circuits and published session times -- and
those are the dates the freeze windows are computed from in ordinary operation.

**The bundled seed is a cold-start fallback only.** `data/seed/calendar-2026.json` was generated by
`tools/generate-seed-calendar.py` from a hand-written table and a standard session timetable. None
of it has been checked against the official calendar. It exists so the tool still works when the
upstream API is unreachable, and so CI is deterministic -- not as a source of truth. Every seeded
event carries `verified: false`, the provider warns once per round when it loads them, and the row
keeps that flag in the database, so a fallback calendar can never be mistaken for a real one.

## What the tests prove

The domain suite is 62 tests with no mocks, no clock and no I/O. Each one is a statement about the
domain, and these are the ones worth reading:

- Back-to-back races leave a 65-hour window for trackside services and nothing more.
- A triple header has no gap long enough for a four-day change, so the engine pushes it past all
  three rounds rather than offering a window that does not fit.
- A sprint weekend's two parc ferme windows open a gap for the race engineers, not for IT: the
  trackside freeze spans the whole weekend regardless.
- A session delayed forty-five minutes by a red flag drags its freeze out with it, instead of
  thawing on the published timetable while the cars are still running.
- A Saturday-night race in Las Vegas keeps a European factory frozen into Sunday morning, which is
  what catches anyone reasoning in circuit-local days.
- The same weekend described in UTC, US central time and Japan standard time produces byte-identical
  freeze windows.
- An event whose parc ferme times have not been published yet falls back to the last session end,
  failing safe rather than quietly dropping the freeze because one field was missing.
- A change that spans a freeze is blocked, never silently trimmed to fit; a change that ends exactly
  as a freeze begins is allowed.
- A service with a long enough load-in lead sees back-to-backs merge into one continuous freeze,
  because reporting two windows with a fictional gap between them would be a lie.
- An unknown service name fails the gate closed, so a typo cannot buy an exemption from a freeze.
- A change touching both a corporate service and a trackside one is judged by the trackside freeze;
  the advisory tier cannot soften it.
- A blocked change is handed a window it would actually fit in: where the 65-hour gap between
  back-to-backs is too short, the suggestion skips past both rounds rather than offering a gap that
  would fail again.
- An emergency change inside a freeze is still blocked, because the override belongs to the approval
  chain that does not exist yet.
- Every state in the lifecycle is reachable from `Draft`, so a state added to the enum without being
  wired into the transition table fails the build.

## Architecture

```
src/
  FreezeManager.Domain/          pure C#, zero dependencies -- freeze engine, state machine
  FreezeManager.Infrastructure/  EF Core over SQLite, calendar providers, sync
  FreezeManager.Api/             minimal API over the lifecycle and the gate
  FreezeManager.Web/             Blazor                                 (phase 5)
tests/
  FreezeManager.Domain.Tests/    fast, deterministic, no I/O
  FreezeManager.Infrastructure.Tests/  in-memory SQLite through the real migrations
  FreezeManager.Api.Tests/       the real HTTP surface, in-memory database
data/seed/                       unverified calendar, illustrative service catalogue
docs/decisions.md                the design decisions, one entry each
```

The domain project has no dependency on EF Core, ASP.NET, HTTP, or the system clock. The freeze
engine is a pure function of `(calendar, service tier, instant)`, which is what makes the test
suite readable as a statement of the domain rather than a set of mocks.

### Calendar ingestion

`IRaceCalendarProvider` has two implementations. `JolpicaCalendarProvider` reads the live API
(Ergast-compatible response shape). `SeedCalendarProvider` reads the bundled file.
`FallbackCalendarProvider` tries the first and, on any failure, uses the second and says so in the
sync history. `CalendarSyncService` reconciles what a provider returns into the database: it
refreshes scheduled times, never overwrites actual times a person recorded, keeps parc ferme
windows a person entered, and skips any event an admin has pinned.

The upstream API publishes session start times only. Durations and parc ferme are filled from
`IngestionDefaults` and every derived parc ferme window is stamped as derived, so a person can tell
a rule's output from a published fact.

## The freeze gate

The gate is the point of the project, so it is worth seeing what it returns. Submitting a change
whose window falls inside a race weekend gets `409` and this:

```json
{
  "title": "Blocked by a change freeze",
  "detail": "Blocked by 1 freeze window(s); next window opens 2026-09-26 18:00Z",
  "reference": "CHG-2026-0001",
  "conflicts": [
    {
      "startUtc": "2026-09-22T08:30:00+00:00",
      "endUtc": "2026-09-26T18:00:00+00:00",
      "reason": "Trackside freeze, load-in to tear-down (Round 17 (Baku City Circuit))",
      "isAdvisory": false,
      "rounds": [17]
    }
  ],
  "suggestedOpenWindow": { "startUtc": "2026-09-26T18:00:00+00:00", "durationHours": 255.5 },
  "retryWith": {
    "method": "POST",
    "path": "/api/changes/CHG-2026-0001/submit",
    "body": {
      "requestedStartUtc": "2026-09-26T18:00:00+00:00",
      "requestedEndUtc": "2026-09-26T22:00:00+00:00"
    }
  }
}
```

`retryWith` is a complete request that would succeed. Posting it back unmodified moves the change
into the next window and submits it, so the compliant path costs one copy rather than a
recalculation. Note that it keeps the change's own four hours rather than expanding to fill the
255-hour gap.

A refusal that does not say what to do instead is the thing people route around, and a control that
is routed around also stops telling you the truth about what is being changed.

### One word per meaning

A freeze window and an open window are opposites, so nothing in the API is called just "window".
Any field holding a period when work is **blocked** says `freezeWindow`; any field holding a period
when work is **allowed** says `openWindow`. So `/api/freeze/status` returns `activeFreezeWindow` and
`nextFreezeWindow`, a gate rejection carries `suggestedOpenWindow`, and the endpoint that finds a
slot is `/api/freeze/next-open-window` returning `nextOpenWindow`. The domain types use the same
names, so there is no translation step between them where a mistake could hide.

### Endpoints

| | |
| --- | --- |
| `POST /api/changes` | Raise a draft. A draft may be incomplete. |
| `GET /api/changes?state=&affectedService=` | List, filtered. |
| `GET` `PUT` `DELETE` `/api/changes/{ref}` | Fetch, replace, delete. Only drafts can be edited or deleted. |
| `POST /api/changes/{ref}/submit` | Runs the freeze gate. Accepts an optional replacement window. |
| `POST /api/changes/{ref}/schedule` | Runs the freeze gate. Accepts an optional replacement window. |
| `POST /api/changes/{ref}/{withdraw,start,complete,fail,roll-back,close,cancel}` | The rest of the lifecycle. |
| `GET /api/freeze/status?serviceKey=&at=` | Is this service frozen, and for how long. Returns `activeFreezeWindow` / `nextFreezeWindow`. |
| `GET /api/freeze/next-open-window?serviceKey=&hours=` | When could a change of this length run. |
| `GET /api/freeze/windows?serviceKey=` | Every window for a service, merged. |
| `GET /api/calendar/{season}` | The stored calendar, with each event's provenance. |
| `POST /api/calendar/{season}/sync` | Pull from the provider and reconcile. |

### Lifecycle

```
Draft -> Submitted -> Scheduled -> Implementing -> Implemented -> Closed
   |          |            |             |
   |          +-> Draft    +-> Submitted +-> Failed -> RolledBack -> Closed
   |                                            |
   +-> Cancelled <-------------------+          +-> Draft
```

The freeze gate runs on `Draft -> Submitted` and `Submitted -> Scheduled`. Illegal moves are
rejected with `409` and a list of the states the change may actually move to. Approval states are
deliberately absent: they insert between `Submitted` and `Scheduled` in phase 4.

## Running it

```bash
dotnet run --project src/FreezeManager.Api
```

On first run it applies migrations, seeds the service catalogue, and pulls the calendar -- live if
the API is reachable, from the bundled seed if not. Either way the attempt is recorded and readable
at `GET /api/calendar/sync-runs`, including the reason for a fallback.

## Build

Requires the .NET 10 SDK.

```bash
dotnet restore FreezeManager.slnx
dotnet build FreezeManager.slnx --configuration Release
dotnet test FreezeManager.slnx --configuration Release
```

One test calls the real calendar API and is skipped by default so CI and offline runs stay
deterministic. To re-run it from a machine that can reach `api.jolpi.ca`:

```bash
FREEZE_LIVE_TESTS=1 dotnet test tests/FreezeManager.Infrastructure.Tests --filter LiveJolpica
```

Schema changes go through EF Core migrations (`dotnet tool install --global dotnet-ef`, then
`dotnet ef migrations add <Name> --project src/FreezeManager.Infrastructure --startup-project
src/FreezeManager.Infrastructure --output-dir Persistence/Migrations`). The infrastructure tests
build their in-memory database through the migrations, so a migration that does not match the
model fails the suite.

## Notes

Tests use xUnit with its built-in assertions and no third-party assertion library, to keep the test
project free of the licence change that affected FluentAssertions v8.
