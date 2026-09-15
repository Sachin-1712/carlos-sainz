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

Built in phases. Current state:

| Phase | Scope | Status |
| --- | --- | --- |
| 0 | Repository scaffold, CI | Done |
| 1 | Domain: race calendar model, freeze engine, test suite | Done |
| 2 | Persistence, calendar ingestion (seeded + live provider) | Not started |
| 3 | API: change request lifecycle and freeze gate | Not started |
| 4 | Approval chains, emergency override, audit hash chain | Not started |
| 5 | Blazor front end | Not started |
| 6 | Continual-improvement metrics, ADRs, demo data | Not started |

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

## Architecture

```
src/
  FreezeManager.Domain/          pure C#, zero dependencies -- freeze engine, state machine
  FreezeManager.Infrastructure/  EF Core, calendar providers            (phase 2)
  FreezeManager.Api/             minimal API, OpenAPI                   (phase 3)
  FreezeManager.Web/             Blazor                                 (phase 5)
tests/
  FreezeManager.Domain.Tests/    fast, deterministic, no I/O
```

The domain project has no dependency on EF Core, ASP.NET, HTTP, or the system clock. The freeze
engine is a pure function of `(calendar, service tier, instant)`, which is what makes the test
suite readable as a statement of the domain rather than a set of mocks.

## Build

Requires the .NET 10 SDK.

```bash
dotnet restore FreezeManager.slnx
dotnet build FreezeManager.slnx --configuration Release
dotnet test FreezeManager.slnx --configuration Release
```

## Notes

Tests use xUnit with its built-in assertions and no third-party assertion library, to keep the test
project free of the licence change that affected FluentAssertions v8.
