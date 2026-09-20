# Design decisions

One entry per decision, written to be said out loud. Each has what was chosen, why, what it would
cost to get wrong, and the sentence to open with if asked.

---

## Phase 1 — domain

### 1. Freeze windows are half-open intervals: `[start, end)`

**Chose:** the end instant of a window is the first instant that is *not* frozen.

**Why:** two windows that touch (one ends 14:00, the next starts 14:00) then merge cleanly instead of
both claiming 14:00, and "frozen until 14:00" means what an engineer assumes it means.

**If wrong:** an off-by-one at every boundary, either double-blocking the boundary instant or leaving
a zero-length gap that the merger then has to special-case.

**Say:** "Half-open, the same convention as every range API. A window ending at 14:00 and one
starting at 14:00 merge rather than overlap."

### 2. Parc fermé is data on the event, not a rule in the engine

**Chose:** each event carries zero or more parc fermé windows. The freeze engine reads them; it never
derives them from session types.

**Why:** the shape of parc fermé on a sprint weekend has been revised more than once as the sporting
regulations changed. If that shape lives in an `if`, a regulation change is a code change and a
release. If it lives in the calendar, it is an edit by a duty manager.

**If wrong:** the engine silently applies last year's parc fermé rule to this year's weekend, and
nobody notices until a freeze lifts early.

**Say:** "Rules that the FIA can change without asking me don't belong in code."

### 3. An unresolvable end anchor falls back rather than opening the freeze

**Chose:** a rule anchored on "parc fermé release" for an event with no parc fermé data falls back to
the last session end. It never yields no window.

**Why:** parc fermé times are frequently published late. A missing field must fail *safe* — a freeze
that runs slightly long is an inconvenience; a freeze that silently does not exist is an outage.

**If wrong:** a data gap in the calendar becomes a hole in the change control.

**Say:** "Missing data lengthens the freeze; it never shortens it."

### 4. Blocking dominates advisory, including through a merge

**Chose:** freeze windows are either blocking or advisory. When windows merge, the result is
advisory only if *every* input was advisory. The calculator merges the two kinds separately so an
advisory window can never widen a blocking one either.

**Why:** corporate services get an advisory nudge, not a hard block, because marking everything
business-critical is how a freeze process loses credibility and gets routed around. But the
softer kind must never be able to soften the harder kind by accident.

**If wrong:** a merge across tiers quietly downgrades a trackside freeze to a warning.

**Say:** "Advisory exists so the process stays credible. Blocking-wins exists so advisory can never
be used to bypass it."

### 5. UTC internally; offsets are a presentation concern

**Chose:** every timestamp is normalised to UTC on the way into the domain. Circuit time zones are
stored as IANA identifiers (strings), not `TimeZoneInfo`, and resolved only when displaying.

**Why:** the US and Europe change their clocks on different weekends. For about a week each spring
and autumn, any code that thinks in "local time" is an hour wrong for one of the two ends of the
telemetry link. Instants don't have this problem.

**If wrong:** an Austin freeze window is quietly an hour off for one week a year, in exactly the week
it matters.

**Say:** "The domain only knows instants. Wall-clock time is something the UI does to an instant."

### 6. Policies are anchors plus signed offsets, not dates

**Chose:** a policy reads "from 48h before the first session until 2h after parc fermé release",
not "from Thursday 12:00". The engine resolves anchors per event.

**Why:** one policy then covers every round for the rest of time, and follows a session when it
moves. A rescheduled qualifying drags every dependent window with it automatically.

**If wrong:** a calendar change means re-entering every window by hand, which means someone
eventually doesn't.

**Say:** "The policy says *where* in a weekend the freeze starts; the calendar says *when*."

### 7. The domain has no clock and no I/O

**Chose:** `FreezeCalculator` is a pure function of `(calendar, tier, instant)`. The caller supplies
the instant. The domain project has zero package references.

**Why:** every scenario in the test suite is reproducible on any day, at any time, with no mocks.
Sixty-two tests run in under 100ms and read as statements about the domain.

**If wrong:** tests that pass on Tuesday and fail on race weekend.

**Say:** "If the interesting logic needs a mock to test, the boundary is in the wrong place."

---

## Phase 2 — persistence and calendar ingestion

### 8. Persistence records are separate classes from domain objects

**Chose:** `RaceEventRecord`, `SessionRecord` etc. are plain settable classes for EF Core. Each has a
`ToDomain()` that calls the validating domain constructor.

**Why:** the domain constructors throw on bad data and expose read-only properties. EF wants
parameterless construction and setters. Sharing one class means one side compromises, and it is
always the domain that loses.

**If wrong:** either the domain grows `{ get; set; }` and stops guaranteeing its invariants, or EF
grows fragile constructor-binding configuration.

**Say:** "The record is the shape the database wants; the domain object is the shape that's
guaranteed valid. The conversion is where validation happens."

### 9. Timestamps are stored as UTC `DateTime`, not `DateTimeOffset`

**Chose:** records hold `DateTime` with `Kind = Utc`. The mapping to the domain wraps them in a
`DateTimeOffset` with zero offset.

**Why:** SQLite has no timestamp type. EF Core stores `DateTimeOffset` as text and cannot translate
ordering or comparison on it, so `OrderBy(x => x.StartUtc)` throws at runtime. Since the domain
already guarantees UTC, the offset carries no information worth the trouble.

**If wrong:** a query that works in tests against an in-memory list fails against the real
database. There is a second, quieter trap: SQLite hands `DateTime` back with `Kind = Unspecified`,
so the context applies a value converter that stamps `Utc` back on every timestamp it reads. The
test that caught it is `Actual_times_recorded_by_a_person_survive_the_next_sync`.

**Say:** "UTC `DateTime` in the store, `DateTimeOffset` at the domain boundary. The offset is always
zero, so nothing is lost."

### 10. The database is the cache; the seed is the cold-start fallback

**Chose:** live calendar data is synced into the database and the application reads from the
database. The bundled seed file is used when the live provider fails and nothing is stored yet.

**Why:** a freeze tool that stops working because a third-party API is down is itself an outage —
and one that hits during a race weekend, when the API is busiest. The last successful sync is the
right thing to serve.

**If wrong:** the tool is unavailable exactly when the constraint it enforces is live.

**Say:** "The calendar API is upstream, not a dependency. We can lose it for a month and still be
right about this weekend."

### 11. Ingestion derives what the source does not publish, and marks it derived

**Chose:** the calendar API publishes session start times only — no durations, no parc fermé. The
ingestion adapter fills those from documented defaults (`IngestionDefaults`) and stamps every
derived parc fermé window `IsDerived = true`.

**Why:** the engine needs end times and parc fermé to do its job, and the honest place to fill a gap
in a source is the adapter for that source, not the engine. The flag means an admin can see at a
glance which windows came from a rule and which from a person.

**If wrong:** either the engine grows source-specific defaults, or derived values are
indistinguishable from published ones and nobody knows what to trust.

**Say:** "Decision 2 says parc fermé is data. Decision 11 is how the data gets there when the
source doesn't have it."

### 12. Provenance travels with the record

**Chose:** every event record carries `Source` (seed / live upstream / admin-edited) and
`IsVerified`. The bundled seed is `IsVerified = false` throughout.

**Why:** the seed dates in this repository were written from memory and have not been checked
against the official calendar. That has to be visible in the data, not just in a README.

**If wrong:** a plausible-looking demo calendar gets treated as a real one.

**Say:** "Every row knows where it came from and whether a human has checked it."

### 13. Sync never overwrites actual times or pinned rows

**Chose:** a sync updates *scheduled* times and replaces *derived* parc fermé windows. It never
touches `ActualStart`/`ActualEnd`, never removes a parc fermé window a person entered, and skips any
event an admin has pinned.

**Why:** the calendar must be editable by a duty manager at 06:00 when a session moves, and that
edit must survive the next automatic sync. Otherwise the tool is bypassed, and a bypassed control
is worse than none.

**If wrong:** the next sync reverts the correction and the freeze is wrong again by lunchtime.

**Say:** "Upstream is the default. A person's correction beats upstream until a person says
otherwise."

### 14. Circuit-to-time-zone is a lookup table with a loud default

**Chose:** a static map from the API's circuit identifiers to IANA zones. Unknown circuits get
`Etc/UTC` and a warning in the sync result.

**Why:** the API does not publish time zones. The table is small, the set of circuits changes by one
or two a year, and a warning at sync time is far better than a wrong zone found on Friday.

**If wrong:** a new circuit silently displays in UTC, which is confusing but not unsafe — the
freeze windows themselves are instants and are unaffected (decision 5).

**Say:** "The engine doesn't need the zone; only the display does. So a miss is loud, not
dangerous."

---

## Phase 3 — change lifecycle and the freeze gate

### 15. The lifecycle is a table, not a chain of conditionals

**Chose:** `ChangeStateMachine` holds a dictionary of state to permitted next states. Every
transition goes through it.

**Why:** the legal moves are then readable in one place, testable exhaustively, and extensible by
editing data. A test walks the table from `Draft` and asserts every state in the enum is reachable,
so adding a state without wiring it up fails the build.

**If wrong:** the rules end up spread across the call sites that enforce them, and the twentieth one
disagrees with the first.

**Say:** "The lifecycle is data. Adding the approval states is an edit to a dictionary, not a
refactor."

### 16. The approval states are deliberately absent, not forgotten

**Chose:** no `Approved` or `Rejected` state yet. `Submitted` goes straight to `Scheduled`.

**Why:** dead enum values that nothing can reach are worse than an honest gap -- they read as
finished work. The states insert between `Submitted` and `Scheduled` when the approval chain lands.

**If wrong:** a reviewer assumes approvals exist and are broken, rather than that they are not built.

**Say:** "The gap is the shape of the next phase. I would rather it be visibly missing than fake."

### 17. A draft may be incomplete; a submission may not

**Chose:** the constructor requires only a title, a requester and a window. The description,
implementation plan and backout plan are required at `EnsureReadyForSubmission`, not at creation.

**Why:** people start writing a change before they have solved it. Demanding a backout plan to open
a draft means the draft gets written in a text file instead, and the tool loses sight of it.

**If wrong:** either the tool is unusable for early thinking, or incomplete changes reach approvers.

**Say:** "Validation belongs at the moment of the promise, not the moment of the first keystroke."

### 18. Completeness is checked before the freeze gate

**Chose:** on a gated transition, `EnsureReadyForSubmission` runs first; only then does the gate.

**Why:** telling someone their window clashes with a race weekend when they have not written a
backout plan is answering a question they have not asked yet. Order the feedback the way the person
will fix it.

**If wrong:** people reschedule around a freeze twice before finding out the real blocker was a
missing field.

**Say:** "Answer the first problem first."

### 19. A refusal always carries the way forward

**Chose:** a blocked submit or schedule returns 409 with the conflicting windows, the next window
long enough for the change, and a `retryWith` object -- a complete request that would succeed.

**Why:** this is the whole argument of the project. A control that only says no gets worked around,
and a control that is worked around also stops telling you the truth about what is being changed.
Taking the compliant path is copying one object into a second call.

**If wrong:** the freeze becomes an obstacle people route around rather than a constraint they plan
against, and the audit trail quietly becomes fiction.

**Say:** "The refusal is the cheapest moment to make the right thing easy. It costs one extra field."

### 20. The suggested retry keeps the change's own duration

**Chose:** `retryWith` proposes the suggested window's start plus the change's existing duration,
not the whole gap.

**Why:** the open window after a race weekend can be ten days long. Expanding a four-hour change to
fill it would be nonsense, and would make the next change look like it has nowhere to go.

**If wrong:** every accepted suggestion books out the entire gap and the calendar congests itself.

**Say:** "It moves the change. It does not resize it."

### 21. An unknown service fails closed

**Chose:** if any affected service is not in the catalogue, the gate returns `UnknownService` and the
transition is refused.

**Why:** an unrecognised name is not evidence that a change is safe. The alternative -- ignoring
services it cannot resolve -- means a typo silently buys an exemption from the freeze.

**If wrong:** a mistyped service name deploys during parc ferme and the control records that it was
fine.

**Say:** "A name I do not recognise is a reason to stop, not a reason to continue."

### 22. A change is judged by every tier it touches

**Chose:** the gate collects the tiers of all affected services and assesses against their merged
windows. `FreezeCalculator` gained tier-collection overloads for this.

**Why:** a change touching mission control and telemetry ingest faces the trackside constraint, not
whichever service happened to be listed first. Merging blocking and advisory separately (decision 4)
means a corporate service in the list cannot soften a trackside freeze.

**If wrong:** the constraint depends on array order, which is the kind of bug that passes review.

**Say:** "The strictest tier wins, and it wins by construction rather than by sorting."

### 23. Which transitions are gated lives in the table too

**Chose:** `ChangeStateMachine.RequiresFreezeCheck` says which moves need the gate. The service asks
the table; it does not decide for itself.

**Why:** a new gated transition cannot then be added without the gate following it. A test asserts
every gated transition is also a legal one.

**If wrong:** someone adds a path to `Scheduled` that skips the freeze check, and nothing catches it.

**Say:** "The state machine owns both questions: can this move happen, and does it need checking."

### 24. Emergency changes are not special yet, and the tests say so

**Chose:** `ChangeType.Emergency` exists and is gated identically to everything else. A test asserts
that an emergency change inside a freeze is blocked.

**Why:** the override is the approval chain's job. Letting emergencies through now, before there is
anyone to approve them or any record that they happened, would be a hole rather than a feature.

**If wrong:** the classification becomes a free bypass for anyone willing to tick the box.

**Say:** "Emergency is a routing decision, not a permission. The permission comes with the chain."

---

## Review fixes before phase 4

### 25. No field in the API is called just "window"

**Chose:** a period when work is blocked is always a `freezeWindow`; a period when work is allowed is
always an `openWindow`. `/api/freeze/status` returns `activeFreezeWindow` and `nextFreezeWindow`; a
rejection carries `suggestedOpenWindow`; the search endpoint is `/api/freeze/next-open-window`
returning `nextOpenWindow`. The domain types were renamed to match.

**Why:** the status response previously had `nextWindow` (the next *freeze*) while an endpoint named
`next-window` returned the next *open slot*. Same words, opposite meanings, one of them about to be
consumed by a UI. In C# the type annotation disambiguated; in JSON there are no types.

**If wrong:** a front end shows "you can deploy from 22 September" on the exact date the freeze
starts. The bug is invisible in review because both readings are grammatical.

**Say:** "Two opposite concepts had one word between them. Naming the type in the field name is free
and makes the wrong reading impossible."

### 26. Unmapped circuits are their own field, not prose in a warning

**Chose:** `CalendarFetchResult` and the sync response carry `unmappedCircuitIds`, alongside the
human-readable warning that already existed.

**Why:** the fix for an unmapped circuit is a row in `CircuitTimeZones`. Anything the operator has to
*act* on should be readable without parsing a sentence -- a warning count told us something was
wrong but not what to add, which cost a round trip to find out.

**If wrong:** unmapped circuits keep silently falling back to UTC because finding out which ones
means reading logs.

**Say:** "A diagnostic should name the thing you have to change, in a field, not in a sentence."

### 27. Query splitting is configured once, not remembered at each call site

**Chose:** `FreezeDbOptions.Apply` sets `QuerySplittingBehavior.SplitQuery` wherever the context is
configured -- the app, the design-time factory, and both test hosts.

**Why:** a race event has both sessions and parc ferme windows, and loading both in one query
multiplies the rows together. That is EF Core's `MultipleCollectionIncludeWarning`. Fixing it with
`AsSplitQuery()` at each call site works until someone writes the next query; fixing it in the
options means they cannot forget. Every affected query has a deterministic `OrderBy`, which is what
split queries need to stay consistent.

**If wrong:** the row count for a season grows as sessions times parc ferme windows, and a warning
the repository claims not to have is printed at every startup.

**Say:** "It was a real warning about a real cartesian product. I fixed it where it cannot come
back rather than where it happened to appear."
