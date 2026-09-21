#!/usr/bin/env bash
#
# Produces a realistic audit trail against a running instance, so a fresh clone has something to
# look at rather than an empty change list and a one-entry log.
#
# It drives the public API only -- the same calls the dashboard makes -- so what you end up with is
# a real trail, not a fixture. Three stories:
#
#   1. A change refused by the freeze, then rescheduled into the window the refusal offered.
#   2. A change that never clashed, approved through its chain and closed.
#   3. An emergency refused, overridden through the full approval chain, and spent.
#
# Usage:  tools/demo-data.sh [base-url]
set -euo pipefail

BASE="${1:-http://127.0.0.1:5000}"
SEASON="${FREEZE_SEASON:-2026}"

need() { command -v "$1" >/dev/null || { echo "need $1 on PATH" >&2; exit 1; }; }
need curl
need python3

api() { curl -sS -X "$1" "$BASE$2" ${3:+-H 'Content-Type: application/json' -d "$3"}; }
field() { python3 -c 'import json,sys; print(json.load(sys.stdin).get(sys.argv[1],""))' "$1"; }

# A demo that prints success while having created nothing is worse than one that stops.
require_ref() { [ -n "$1" ] || { echo "$2" >&2; exit 1; }; }

echo "==> checking $BASE"
curl -sf "$BASE/api" >/dev/null || { echo "no instance at $BASE -- start it with: dotnet run --project src/FreezeManager.Api" >&2; exit 1; }

# Find a window that is genuinely frozen, and one that is genuinely open, from the live calendar
# rather than from hard-coded dates that rot the moment the season changes.
echo "==> locating a real freeze window for telemetry-ingest"
WINDOWS=$(curl -sS "$BASE/api/freeze/windows?serviceKey=telemetry-ingest")

read -r FROZEN_START FROZEN_END OPEN_START <<EOF
$(python3 - "$WINDOWS" <<'PY'
import json, sys
from datetime import datetime, timedelta, timezone
wins = [w for w in json.loads(sys.argv[1]) if not w["isAdvisory"]]
now = datetime.now(timezone.utc)
future = [w for w in wins if datetime.fromisoformat(w["endUtc"]) > now]
if not future:
    print("NONE NONE NONE"); raise SystemExit
w = future[0]
start = datetime.fromisoformat(w["startUtc"])
end = datetime.fromisoformat(w["endUtc"])
inside = start + (end - start) / 2
fmt = lambda d: d.strftime("%Y-%m-%dT%H:%M:%SZ")
print(fmt(inside), fmt(inside + timedelta(hours=4)), fmt(end + timedelta(hours=1)))
PY
)
EOF

if [ "$FROZEN_START" = "NONE" ]; then
  echo "no upcoming freeze window found; is the calendar loaded?" >&2
  exit 1
fi

echo "    frozen sample:  $FROZEN_START -> $FROZEN_END"

# ---------------------------------------------------------------- 1. refused, then rescheduled
echo "==> 1/3  a change refused by the freeze, then moved to the window it was offered"
REF1=$(api POST /api/changes "{
  \"title\":\"Patch telemetry ingest nodes\",\"requestedBy\":\"a.mccall\",
  \"description\":\"Apply the vendor security patch to all four ingest nodes.\",
  \"implementationPlan\":\"Rolling restart, one node at a time, checking packet loss between each.\",
  \"backoutPlan\":\"Restore the previous image and fail back to the standby.\",
  \"type\":\"Normal\",\"impact\":\"Medium\",\"likelihood\":\"Low\",
  \"affectedServiceKeys\":[\"telemetry-ingest\"],
  \"requestedStartUtc\":\"$FROZEN_START\",\"requestedEndUtc\":\"$FROZEN_END\"}" | field reference)

require_ref "$REF1" "story 1 could not raise its change"

RETRY=$(curl -sS -o /tmp/freeze-demo-blocked.json -w '%{http_code}' -X POST "$BASE/api/changes/$REF1/submit")
echo "    $REF1 submit -> HTTP $RETRY (refused, as intended)"

python3 -c 'import json;d=json.load(open("/tmp/freeze-demo-blocked.json"));print("    reason:",d["conflicts"][0]["reason"])'
BODY=$(python3 -c 'import json;print(json.dumps(json.load(open("/tmp/freeze-demo-blocked.json"))["retryWith"]["body"]))')

api POST "/api/changes/$REF1/submit" "$BODY" >/dev/null
echo "    replayed the suggested window -> submitted"

for ROLE in TracksideItLead HeadOfIt RaceEngineeringNominee; do
  api POST "/api/changes/$REF1/approvals" "{\"role\":\"$ROLE\",\"approver\":\"$ROLE.person\",\"decision\":\"Approved\"}" >/dev/null
done
api POST "/api/changes/$REF1/schedule" "" >/dev/null || true
echo "    approved by the trackside chain and scheduled"

# ---------------------------------------------------------------- 2. clean run to closed
echo "==> 2/3  a corporate change that never clashed, run through to closed"

# Ask the engine for a window that is genuinely open for this service rather than guessing one.
read -r OPEN_FROM OPEN_TO <<EOF
$(curl -sS "$BASE/api/freeze/next-open-window?serviceKey=intranet&hours=2" | python3 -c '
import json, sys
from datetime import datetime, timedelta
w = json.load(sys.stdin).get("nextOpenWindow")
if not w:
    print("NONE NONE"); raise SystemExit
start = datetime.fromisoformat(w["startUtc"])
fmt = lambda d: d.strftime("%Y-%m-%dT%H:%M:%SZ")
print(fmt(start), fmt(start + timedelta(hours=2)))')
EOF

if [ "$OPEN_FROM" = "NONE" ]; then
  echo "no open window for intranet; skipping story 2" >&2
else
  REF2=$(api POST /api/changes "{
    \"title\":\"Rotate intranet TLS certificate\",\"requestedBy\":\"j.patel\",
    \"description\":\"Annual certificate rotation for the staff intranet.\",
    \"implementationPlan\":\"Install the renewed certificate and reload the proxy.\",
    \"backoutPlan\":\"Reinstate the previous certificate from the vault.\",
    \"type\":\"Normal\",\"impact\":\"Low\",\"likelihood\":\"Low\",
    \"affectedServiceKeys\":[\"intranet\"],
    \"requestedStartUtc\":\"$OPEN_FROM\",\"requestedEndUtc\":\"$OPEN_TO\"}" | field reference)

  require_ref "$REF2" "story 2 could not raise its change"

  api POST "/api/changes/$REF2/submit" "" >/dev/null
  api POST "/api/changes/$REF2/approvals" '{"role":"ServiceOwner","approver":"s.owner","decision":"Approved"}' >/dev/null
  for STEP in schedule start complete close; do api POST "/api/changes/$REF2/$STEP" "" >/dev/null; done

  STATE=$(curl -sS "$BASE/api/changes/$REF2" | field state)
  echo "    $REF2 approved, scheduled, implemented and closed (state: $STATE)"
fi

# ---------------------------------------------------------------- 3. the emergency
echo "==> 3/3  an emergency refused, overridden through the full chain, and spent"
REF3=$(api POST /api/changes "{
  \"title\":\"Restart telemetry ingest node 3\",\"requestedBy\":\"s.sindhe\",
  \"description\":\"Node 3 is dropping packets mid-session and the standby has already failed over.\",
  \"implementationPlan\":\"Fail over to the standby, restart node 3, verify packet loss, fail back.\",
  \"backoutPlan\":\"Leave the standby serving and take node 3 out of the pool.\",
  \"type\":\"Emergency\",\"impact\":\"High\",\"likelihood\":\"High\",
  \"affectedServiceKeys\":[\"telemetry-ingest\"],
  \"requestedStartUtc\":\"$FROZEN_START\",\"requestedEndUtc\":\"$FROZEN_END\"}" | field reference)

require_ref "$REF3" "story 3 could not raise its change"

curl -sS -o /dev/null -X POST "$BASE/api/changes/$REF3/submit"
echo "    $REF3 refused -- an emergency gets no free pass through the gate"

api POST "/api/changes/$REF3/override" "{
  \"incidentReference\":\"INC-4471\",\"requestedBy\":\"s.sindhe\",
  \"justification\":\"Node 3 is dropping packets mid-session and the standby has already failed over.\"}" >/dev/null
echo "    override requested against INC-4471"

for ROLE in TracksideItLead HeadOfIt RaceEngineeringNominee; do
  api POST "/api/changes/$REF3/override/approvals" "{\"role\":\"$ROLE\",\"approver\":\"$ROLE.person\",\"decision\":\"Approved\"}" >/dev/null
  echo "    approved by $ROLE"
done

api POST "/api/changes/$REF3/submit" "" >/dev/null
echo "    submitted under override -- the grant is now spent"

# ---------------------------------------------------------------- what you ended up with
echo
echo "==> audit trail"
curl -sS "$BASE/api/audit" | python3 -c '
import json, sys
for e in json.load(sys.stdin):
    print("  #%-3d %-26s %-24s %s" % (e["sequence"], e["action"], e["actor"], e["subject"]))'

echo
echo "==> chain verification"
curl -sS "$BASE/api/audit/verify" | python3 -c '
import json,sys;d=json.load(sys.stdin)
print("  valid: %s across %d entries" % (d["isValid"], d["entryCount"]))'

echo
echo "Open $BASE/ to see it, or $BASE/scalar/v1 for the API."
