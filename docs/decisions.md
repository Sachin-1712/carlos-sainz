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

---

## Phase 4 — approvals, override and audit

### 28. The approval chain scales with the strictest tier a change touches

**Chose:** corporate needs the service owner; race support adds the head of IT; trackside adds a
race engineering nominee. A change touching several tiers takes the chain of the strictest, the same
rule the freeze itself uses (decision 22).

**Why:** the chain should track the consequence of getting it wrong. Breaking the intranet is an IT
problem. Breaking telemetry during a session is a sporting one, and the people who carry that
consequence should have a say before it happens, not after.

**If wrong:** either every trivial change queues behind three approvers until people route around
the process, or a trackside change ships on one person's judgement.

**Say:** "The chain is as long as the blast radius. Roles, not names -- a chain written in names
stops working the day someone leaves."

### 29. A standard change is pre-approved, but not pre-permitted

**Chose:** `ChangeType.Standard` yields an empty chain and auto-advances to `Approved` on
submission. It is still gated by the freeze, still audited, and an *override* for one still needs an
approver.

**Why:** that is what "standard change" means in ITSM -- the risk was assessed once, in advance. But
being pre-approved is about the work; going through a freeze is a separate question that nobody
pre-approved.

**If wrong:** classifying a change as standard becomes a way to deploy during parc ferme unattended.

**Say:** "Pre-approved covers the change. It does not cover the weekend."

### 30. Approvals are discarded when a change returns to draft

**Chose:** `TransitionTo(Draft)` clears the recorded approvals.

**Why:** an approval is of a specific change, not of a change reference. Without this, someone
approves a one-line config edit, the author withdraws it, rewrites it as a schema migration, and
resubmits carrying the old approval.

**If wrong:** the audit trail shows three people approving something none of them read.

**Say:** "Approval attaches to what was written, not to the ticket number."

### 31. An override is attributable, time-boxed and accountable

**Chose:** every override requires a linked incident reference and a justification of at least 30
characters; a grant expires on its own (2 hours by default, 12 maximum); and it takes the same
approval chain the change's tier demands.

**Why:** those three properties are what separate a control from a bypass. Without attribution you
cannot review it; without expiry a standing exemption accumulates; without a chain it is one
person's decision at 2am.

**If wrong:** the override becomes the normal path, and the freeze becomes advisory in practice
while still claiming to block.

**Say:** "An override is not permission to ignore the freeze. It is a recorded, expiring, approved
exception to it."

### 32. Expiry is an event, not a calculation

**Chose:** a sweeper notices elapsed grants, moves them to `Expired`, and writes an audit entry. A
flag on the row means exactly one entry is written no matter how often the sweep runs.

**Why:** an override that quietly stops working leaves no trace that permission was ever held.
Inferring expiry from a timestamp at read time gives you the current state but no history, and the
question a reviewer asks is "how many overrides were granted last month and what happened to them".

**If wrong:** the log records grants and uses but never the grants nobody used, so the override rate
looks lower than it was.

**Say:** "If it only exists as a comparison against the clock, it never happened as far as the
record is concerned."

### 33. Break-glass is a narrower door, not an open one

**Chose:** a single approver, but only the head of IT or the trackside IT lead, and it costs a
mandatory retrospective within 24 hours. Overdue retrospectives are flagged by the same sweeper.

**Why:** pretending break-glass does not happen is how you get shadow processes -- at 02:00 in
Suzuka you cannot assemble three people, and someone will act anyway. Better to give that a lit path
with a bill attached than to leave it outside the system.

**If wrong:** either the emergency path is unusable when it is actually needed, or it becomes the
cheap way to skip the chain.

**Say:** "It exists because the alternative is people working around the tool. The retrospective is
what stops it becoming the default."

### 34. Audit entries are hash-chained, and the claim is detection, not prevention

**Chose:** each entry's SHA-256 covers its own contents and the previous entry's hash. Fields are
length-prefixed before hashing. `GET /api/audit/verify` walks the chain and reports where it first
breaks.

**Why:** length prefixes matter more than they look. With a plain separator, an actor `"a|b"` with
subject `"c"` hashes identically to actor `"a"` with subject `"b|c"` -- a forgery needing no key.
And the honest claim is detection: someone with write access could recompute the whole chain. The
response says so in a `scope` field rather than letting a green tick be over-read.

**If wrong:** the project claims tamper-proofing it does not have, which is worse than claiming
nothing.

**Say:** "It makes tampering detectable and expensive, not impossible. Entries removed from the end
cannot be detected by a chain alone, and the endpoint says that out loud."

### 35. Append-only is enforced in the data layer, not by convention

**Chose:** a `SaveChanges` interceptor throws on any update or delete of an audit row.
`UseFreezeDefaults` bundles it with the provider options so every host gets it.

**Why:** the chain detects tampering after the fact; the interceptor stops the ordinary way of doing
it happening at all. Bundling it into the options is the part that matters: when it was applied only
in `Program.cs`, the test hosts silently lacked the guarantee, and a test caught it.

**If wrong:** a stray `SaveChanges` on a tracked audit entity rewrites history, and only the next
verification notices.

**Say:** "Two layers. The interceptor stops the application; the hash chain catches anyone who goes
around it."

### 36. A success that needed an override says so in the response

**Chose:** when the gate refuses and a granted override carries the change through, the response
carries `proceededUnderOverride` with the incident reference alongside the `Blocked` gate outcome.

**Why:** the raw response otherwise reads `state: Submitted` next to `freezeGate: Blocked`, and a
caller has to infer what happened. That is the same class of ambiguity as the `nextWindow` naming
collision (decision 25), caught the same way -- by reading real output rather than a test.

**If wrong:** a UI shows a success and a refusal side by side and picks one.

**Say:** "Both facts are true: the gate refused, and it went ahead. The response states both rather
than leaving the reader to reconcile them."

---

## Phase 5 — dashboard and browsable API

### 37. A missing round is shouted about, because nothing inside the engine can fail safe for it

**Chose:** `CalendarCompleteness` inspects the stored calendar for gaps. Startup logs a warning
naming the missing rounds; `GET /api/calendar/{season}/completeness` returns **409** when a whole
round is absent, and for each one says whether ingestion dropped it or upstream never sent it.
`SkippedRounds` records the former with a reason, persisted on the sync run.

**Why:** decision 3 makes missing data *within* an event fail safe by lengthening the freeze. Nothing
equivalent is possible for an event the engine has never seen: a round that never loaded is simply a
weekend on which everything looks deployable. The only defence is to say so where someone reads it.
And the two causes need different fixes -- a dropped round is an ingestion bug, an absent one is an
upstream gap -- so the diagnostic distinguishes them rather than leaving it to be guessed.

**If wrong:** the tool reports "no freeze" for a race weekend and is confidently wrong, which is the
one failure mode the whole project exists to prevent.

**Say:** "Everything else fails safe by over-freezing. A missing round can't, so it gets shouted
about instead -- and the message says which of the two causes it is."

### 38. A cancelled round is not a hole

**Chose:** completeness counts every round in the calendar, including cancelled ones. Only rounds
absent from the calendar entirely are reported missing.

**Why:** a cancelled event is a deliberate exclusion that somebody recorded. Treating it as a gap
would cry wolf on the one signal that has to stay trustworthy.

**If wrong:** every cancelled race raises a false alarm and people learn to ignore the alarm.

**Say:** "Recorded-and-excluded is not the same as never-loaded. Only the second is a problem."

### 39. The compliant window is on screen before anyone types a date

**Chose:** ticking a service recalculates the next open window for its tiers immediately, with a
button that moves the form to it. A refusal replaces that suggestion with its own, so there are
never two competing buttons.

**Why:** this is decision 19 carried into the interface. A refusal that arrives *after* someone has
filled in a form teaches them the tool is an obstacle; a suggestion that arrives *before* they type
a date makes the compliant path the lazy one.

**If wrong:** the UI becomes a way to discover you were wrong, rather than a way to be right first
time.

**Say:** "The cheapest moment to make the right thing easy is before the person has committed to the
wrong one."

### 40. The page can be asked about an instant other than now

**Chose:** an "evaluate at" control, also reachable as `?at=`, moves the whole page to a chosen
instant.

**Why:** "what is frozen next Friday?" is the planning question, and the engine is already a pure
function of an instant (decision 7). Exposing that costs a query parameter, and it is also what
makes a live freeze demonstrable rather than a thing you have to wait for a race weekend to see.

**If wrong:** the tool only answers questions about the present, which is the least useful tense for
change planning.

**Say:** "The engine never assumed 'now'. The page shouldn't either."

### 41. Status colour is never the only signal

**Chose:** frozen, advisory and open each carry an icon and a word as well as a colour, in the table
and in the timeline legend.

**Why:** the status palette's warning step sits below 3:1 contrast on a light surface by design, and
colour alone excludes colourblind readers regardless of contrast. Icon plus label is the documented
mitigation, so it is not optional.

**If wrong:** the single most important fact on the page -- can I deploy or not -- is invisible to
some readers.

**Say:** "Colour is the fastest channel, not the only one. Every status mark says what it is in
words too."

### 42. Static web assets are composed in every environment

**Chose:** `builder.WebHost.UseStaticWebAssets()`, not only the Development default.

**Why:** `dotnet run` defaults to Production when there is no launch profile. Without this the page
renders from the prerender and then 404s on `blazor.web.js`, so it looks fine and does nothing --
the worst kind of broken, and precisely what a reviewer cloning the repository would hit.

**If wrong:** the first thing a reviewer sees is a dashboard whose buttons do not work.

**Say:** "It rendered and it was dead. Found it by driving the real browser rather than trusting the
200."

---

## Post-review fixes

### 43. The seed is generated from a verified sync, not maintained by hand

**Chose:** `SeedExporter` writes the bundled seed out of a calendar that has already been synced.
`GET /api/calendar/{season}/seed-export` returns it; one redirect refreshes the file.

**Why:** the 2026 season changed under the project — two rounds cancelled, one replaced — and the
hand-written seed did not. A stale fallback is not an inert file: when upstream is unreachable the
engine falls back to it and computes freeze windows for races that are not happening while missing
ones that are. Generating it from real data makes refreshing it one command instead of an editing
exercise nobody remembers to do.

**If wrong:** the fallback silently describes a season that no longer exists, which is the same
failure as a missing round except it looks fine.

**Say:** "A fallback nobody refreshes is a liability. Making it a build artifact of a real sync is
what stops it rotting."

### 44. Seed drift is checked against the last sync from a published source

**Chose:** every sync records how many active rounds it stored and whether it came from upstream.
The seed's round count is compared against the most recent verified one, at startup and at
`GET /api/calendar/{season}/seed-drift`.

**Why:** "is the fallback still right?" has to be answerable without a race weekend to prove it
wrong. Comparing against the last *verified* sync specifically matters, because comparing against
the seed's own fallback data would compare the file to itself and always agree.

**If wrong:** drift is discovered during the outage the fallback exists for.

**Say:** "The seed is checked against the last thing we know was real, not against whatever we
happen to be running on."

### 45. Sync runs are ordered by timestamp and then identity

**Chose:** every "most recent run" query orders by `StartedAtUtc` **and then** by `Id`, both
descending.

**Why:** two syncs can share a timestamp — a re-sync moments after the first, or any test with a
fixed clock — and ordering by timestamp alone then returns an arbitrary one of them. A drift check
that reads the wrong run reports clean when it is not.

**If wrong:** the check is right most of the time, which is the worst failure mode for a check.

**Say:** "Found it because two runs in a fixed-clock test tied, and the tie was being broken by
whatever the database felt like returning."

### 46. The emergency override is reachable from the interface

**Chose:** when an emergency change is refused, the page offers the override: incident reference,
justification, the approval chain for that tier with a control per role, and the grant with its
expiry on screen. Submitting under the grant is the *same* call as an ordinary submit.

**Why:** an override that only exists over HTTP is a feature nobody can be shown, and the override
is the part that makes the freeze a control rather than a wall. Keeping the submit call identical
matters too: the gate still refuses, and the grant is what carries the change past it — which is
why the result reports both facts rather than pretending the gate allowed it.

**If wrong:** the emergency path is either undemonstrable or, worse, reimplemented in the UI as a
second code path that skips the gate.

**Say:** "Same call, same refusal. The grant is what changes the outcome, and it says so."

### 47. A grant's clock is the real one, even when the page is reasoning about another instant

**Chose:** the dashboard can evaluate any instant, but an override's remaining time is measured
against the system clock, and the page says so when the two differ.

**Why:** the evaluated instant is a hypothesis; a grant is a real thing with a real expiry. Mixing
them produced "expires in -74.8 h", which is the sort of output that makes someone stop trusting
every other number on the page.

**If wrong:** the most safety-critical number in the interface is nonsense in exactly the mode
someone would use to plan.

**Say:** "Two different clocks were on screen. One is a what-if and one is a countdown, so they are
labelled and measured separately."

### 48. A demo script that drives the real API

**Chose:** `tools/demo-data.sh` produces the audit trail by making the same calls the dashboard
makes, and finds its windows by asking the engine which ones are frozen rather than hard-coding
dates.

**Why:** a fixture inserted straight into the database would prove nothing and would rot the moment
the calendar moved. Driving the API means the demo is an integration test with nice output, and it
keeps working across a season change.

**If wrong:** the demo data shows a trail the application could not actually have produced.

**Say:** "It is the real flow, so it is also a smoke test. It found a bug the first time I ran it."
