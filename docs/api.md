# API reference

A browsable version runs at `/scalar/v1` when the application is up. This is the flat list.

## One word per meaning

A freeze window and an open window are opposites, so nothing in the API is called just "window".
Any field holding a period when work is **blocked** says `freezeWindow`; any field holding a period
when work is **allowed** says `openWindow`. The domain types use the same names, so there is no
translation step between them where a mistake could hide.

## Where to start

| | |
| --- | --- |
| `GET /` | The dashboard. |
| `GET /scalar/v1` | This list, browsable, with schemas and a try-it button. |
| `GET /api` | Service index: season, where the docs are, the endpoints worth knowing. |

## Changes

| | |
| --- | --- |
| `POST /api/changes` | Raise a draft. A draft may be incomplete. |
| `GET /api/changes?state=&affectedService=` | List, filtered. |
| `GET` `PUT` `DELETE` `/api/changes/{ref}` | Fetch, replace, delete. Only drafts can be edited or deleted. |
| `POST /api/changes/{ref}/submit` | Runs the freeze gate. Accepts an optional replacement window. |
| `POST /api/changes/{ref}/schedule` | Runs the freeze gate. Accepts an optional replacement window. |
| `POST /api/changes/{ref}/{withdraw,start,complete,fail,roll-back,close,cancel}` | The rest of the lifecycle. |

## Approvals and overrides

| | |
| --- | --- |
| `GET` `POST` `/api/changes/{ref}/approvals` | The chain this change needs; record one role's decision. |
| `GET` `POST` `/api/changes/{ref}/override` | Overrides raised; raise an emergency override. |
| `POST /api/changes/{ref}/override/approvals` | Approve or reject an override. |
| `POST /api/changes/{ref}/override/revoke` | Withdraw an override before it expires. |
| `POST /api/changes/{ref}/override/retrospective` | Complete the retrospective a break-glass owes. |

## Freeze queries

| | |
| --- | --- |
| `GET /api/freeze/status?serviceKey=&at=` | Is this service frozen, and for how long. Returns `activeFreezeWindow` / `nextFreezeWindow`. |
| `GET /api/freeze/next-open-window?serviceKey=&hours=` | When could a change of this length run. |
| `GET /api/freeze/windows?serviceKey=` | Every freeze window for a service, merged. |
| `GET /api/freeze/services` | The service catalogue and each service's tier. |

## Calendar

| | |
| --- | --- |
| `GET /api/calendar/{season}` | The stored calendar, with each event's provenance. |
| `GET /api/calendar/{season}/completeness` | Missing or incomplete rounds, and which cause applies. |
| `POST /api/calendar/{season}/sync` | Pull from the provider and reconcile. |
| `GET /api/calendar/{season}/seed-export` | Regenerate the bundled seed from the stored calendar. |
| `GET /api/calendar/{season}/seed-drift` | Has the seed drifted from the last verified sync. |
| `GET /api/calendar/sync-runs` | Recent sync attempts, successful or not, with the reason for any fallback. |

## Audit

| | |
| --- | --- |
| `GET /api/audit?subject=` | The audit log, in sequence order. |
| `GET /api/audit/verify` | Walk the hash chain and report any alteration. |
| `POST /api/audit/sweep-overrides` | Run the expiry sweep now. Also runs on a timer. |

## The change lifecycle

```
Draft -> Submitted -> Approved -> Scheduled -> Implementing -> Implemented -> Closed
   |          |           |            |             |
   |          +-> Rejected|            +-> Approved  +-> Failed -> RolledBack -> Closed
   |          |           |                                |
   |          +-> Draft <-+--------------------------------+
   |
   +-> Cancelled
```

The freeze gate runs on `Draft -> Submitted` and `Approved -> Scheduled` — the two points at which
a window is claimed. Approval sits between them and does not re-run the gate, because approving a
change agrees to the work, not to the slot. The transitions are a table rather than a chain of
conditionals, which is why inserting the approval states was an edit to data.

Illegal moves return `409` with the list of states the change may actually move to.
