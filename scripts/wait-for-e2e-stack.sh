#!/usr/bin/env bash
# Waits for the Aspire RUN-mode stack to be ready for the Playwright e2e suite
# (ADR-0108): management-web serving on :5173, and the gateway routing to a
# context service (so OIDC + gateway + services are warm). Best-effort on the
# gateway probe — if it can't be read it falls back to a buffer and lets
# Playwright's CI retries absorb any residual warm-up.
set -uo pipefail

WEB="http://localhost:5173"
WS="${GITHUB_WORKSPACE:-$PWD}"

# The AppHost's own composed resource set, first of all — it is the question
# whose failure explains every other probe in this file: a resource that never
# started (minio FailedToStart, audit-observability still Waiting on it) used
# to satisfy every probe below, including the migration one, because none of
# them ask the AppHost anything (#2268). The AppHost writes this file
# (StackStatusReport.cs, switched on by StackStatusFile=) naming every
# resource it composed and the state each reached; the gate here only reads
# it.
#
# "Started" is Running, or Finished/Exited for the one-shot `migrations`
# resource with no *confirmed* non-zero exit code — the probe below asks a
# different question about that same resource (not "did it start" but "did it
# leave a migrated schema behind"). An empty exit code counts as started, not
# failed: AspireFixture.cs:637-648 (#2064) documents that ResourceEvent
# delivery can carry a Finished state before its ExitCode is populated in the
# same snapshot, so treating "unconfirmed" as "not started" would burn the
# whole wait window on a migrations run that already succeeded. Everything
# else — Waiting, Starting, NotStarted, FailedToStart, Exited,
# RuntimeUnhealthy, Terminated, a non-migrations resource that is Finished or
# Exited at all, or any state this script does not recognise — counts as not
# started.
#
# Like the migration probe, this is **not** best-effort: "I could not ask" is
# not "the stack came up", so a status report that never appears is fatal
# rather than skipped.
STATUS_FILE="${STACK_STATUS_FILE:-${GITHUB_WORKSPACE:-$PWD}/stack-status.tsv}"

# Echoes the report's `<name>\t<state>\t<exit code>` lines, skipping the `#
# stack-status ...` header.
status_lines() {
  grep -v '^#' "$STATUS_FILE" 2>/dev/null
}

# Echoes one ` <name>(<state>)` per resource that has not started, and nothing
# when every one of them has — the same rendering `unmigrated_databases` below
# uses, so the two failure messages read consistently.
#
# The `migrations` exemption below reaches this function only with an empty
# or "0" exit code — dead_resources() above already fatal'd any confirmed
# non-zero exit before this ever runs — mirroring
# AspireFixture.IsHealthy's `!ExitedNonZero` clause (#2064), scoped the same
# way it is there: to the one one-shot resource this composition has, so a
# persistent service that unexpectedly stops is still reported, not quietly
# waved through.
not_started_resources() {
  while IFS=$'\t' read -r name state exit_code; do
    [ -n "$name" ] || continue
    if [ "$state" = "Running" ]; then continue; fi
    if [ "$state" = "Finished" ] && [ "$exit_code" = "0" ]; then continue; fi
    case "$name" in
      migrations | migrations-*)
        case "$state" in
          Finished | Exited) continue ;;
        esac
        ;;
    esac
    printf ' %s(%s)' "$name" "$state"
  done <<< "$(status_lines)"
}

# Echoes one ` <name>(<state>[, exit <exit code>])` per resource in a state
# that cannot become Running on its own — fatal on sight, so it does not cost
# the rest of this file's wait budget on services that WaitFor() it (a dead
# `migrations` run behind nine WaitFor()s is the case that matters most).
#
# Finished/Exited only count once a non-zero exit code is confirmed — an empty
# exit code means "ended, cause unread", not "ended cleanly" (plan.md §2.4:
# never print 0 for unknown), so it falls through to the poll instead of
# being asserted as a definite failure here.
#
# FailedToStart and Terminated need no exit-code check: neither state ever
# carries one (no process ran, or none Aspire observed exiting), and both are
# unconditionally terminal — mirrors
# AspireFixture.FatalStartupStates/KnownResourceStates.TerminalStates.
# RuntimeUnhealthy is deliberately excluded, matching that same file's
# judgement: a resource answering badly may still recover, so it stays in
# not_started_resources and gets the rest of the poll window rather than a
# verdict this function is not confident enough to make.
dead_resources() {
  while IFS=$'\t' read -r name state exit_code; do
    [ -n "$name" ] || continue
    case "$state" in
      Finished | Exited)
        if [ -n "$exit_code" ] && [ "$exit_code" != "0" ]; then
          printf ' %s(%s, exit %s)' "$name" "$state" "$exit_code"
        fi
        ;;
      FailedToStart | Terminated)
        printf ' %s(%s)' "$name" "$state"
        ;;
    esac
  done <<< "$(status_lines)"
}

echo "Waiting for the AppHost's composed resource set to start ..."
resources_ready=0
for i in $(seq 1 120); do # up to ~10 min
  if [ ! -f "$STATUS_FILE" ]; then
    sleep 5
    continue
  fi

  # A status report with zero resource lines — the header only, or the file
  # existing but unreadable (status_lines() silences grep's stderr and exit
  # status on purpose, to tolerate a read racing the writer's atomic
  # File.Move) — must not read as "everything started": both
  # dead_resources() and not_started_resources() print nothing over an empty
  # input, and this loop would otherwise treat that silence as success on the
  # very next check.
  if [ -z "$(status_lines)" ]; then
    sleep 5
    continue
  fi

  dead="$(dead_resources)"
  if [ -n "$dead" ]; then
    echo "::error::a composed resource ended without starting —$dead"
    exit 1
  fi

  not_started="$(not_started_resources)"
  if [ -z "$not_started" ]; then
    echo "  every composed resource started after ~$((i * 5))s"
    resources_ready=1
    break
  fi
  sleep 5
done
if [ "$resources_ready" != "1" ]; then
  if [ ! -f "$STATUS_FILE" ]; then
    echo "::error::no status report ever appeared at $STATUS_FILE — the boot step must pass StackStatusFile= so the AppHost writes one"
    exit 1
  fi
  if [ -z "$(status_lines)" ]; then
    echo "::error::the status report at $STATUS_FILE names no resources — it may be unreadable, or the AppHost has not written past its header line"
    exit 1
  fi
  echo "::error::not every composed resource started —$(not_started_resources)"
  exit 1
fi

# The migration run, first of the schema-level probes. It is the cheapest question and the one whose
# failure explains the others: a stack whose migrations aborted used to satisfy
# every probe below it, and the Playwright suite then failed in ways that
# describe the product rather than the boot (#2137).
#
# Asked of the databases rather than of the `migrations` resource, because that
# resource is a process Aspire supervises and nothing on this side of the
# dashboard can read its exit code. An abort leaves the contexts the runner
# reached migrated and the ones after it without a history table at all, which
# is what this catches. A run that aborts on the *last* migration of the last
# context leaves a populated table and is not caught; pinning the expected
# count per context would instead go red on every migration anyone adds.
#
# Unlike the gateway probe below, this one is **not** best-effort. "I could not
# ask" is not "the schema is there".
DATABASES="camera-catalog-db stream-distribution-db layout-composition-db \
overlay-designer-db system-variables-db event-ingestion-db automation-db \
identity-db audit-db"

# `^postgres` so pgadmin, which a dev-mode stack also runs, cannot match.
postgres_container() {
  cid="$(docker ps --filter 'name=^postgres' --format '{{.ID}}' 2>/dev/null)"
  printf '%s' "${cid%%$'\n'*}"
}

# One exec for all nine databases, not one each: this runs every five seconds
# for up to five minutes, and nine round-trips a poll is 540 of them. The
# password comes out of the container's own environment, so no credential lives
# in this file. Answers `<database>=<rows>`, with `none` where psql could not.
migration_rows() {
  docker exec "$1" sh -c '
    for db in '"$DATABASES"'; do
      rows="$(PGPASSWORD="$POSTGRES_PASSWORD" psql -U "${POSTGRES_USER:-postgres}" \
        -d "$db" -tAc "select count(*) from \"__EFMigrationsHistory\"" 2>/dev/null \
        | tr -d "[:space:]")"
      printf "%s=%s\n" "$db" "${rows:-none}"
    done' 2>/dev/null
}

# Echoes one ` <database>(<reason>)` per database that is not migrated, and
# nothing at all when every one of them is.
unmigrated_databases() {
  while IFS='=' read -r db rows; do
    [ -n "$db" ] || continue
    case "$rows" in
      '' | *[!0-9]*) printf ' %s(no history table)' "$db" ;;
      0) printf ' %s(0 applied)' "$db" ;;
    esac
  done <<< "$(migration_rows "$1")"
}

echo "Waiting for the migration run to finish ..."
migrated=0
unmigrated=' (no postgres container)'
for i in $(seq 1 60); do # up to ~5 min
  cid="$(postgres_container)"
  if [ -n "$cid" ]; then
    unmigrated="$(unmigrated_databases "$cid")"
    if [ -z "$unmigrated" ]; then
      echo "  every database carries applied migrations after ~$((i * 5))s"
      migrated=1
      break
    fi
  fi
  sleep 5
done
if [ "$migrated" != "1" ]; then
  echo "::error::the migration run did not finish —$unmigrated"
  exit 1
fi

echo "Waiting for management-web on :5173 ..."
web_up=0
for i in $(seq 1 150); do # up to ~12.5 min
  if curl -fsS -o /dev/null --max-time 4 "$WEB/" 2>/dev/null; then
    echo "  management-web serving after ~$((i * 5))s"
    web_up=1
    break
  fi
  sleep 5
done
if [ "$web_up" != "1" ]; then
  echo "::error::management-web never served on :5173"
  exit 1
fi

# The two kiosk instances. **Waited for by name, because there are now three
# front ends and only one of them was ever checked.** The wall display (spec 052)
# is the same bundle in wall mode; it starts last and a suite that began before
# it was listening would fail every wall test for a reason that has nothing to do
# with the product.
for port in 5174 5175; do
  echo "Waiting for the kiosk on :$port ..."
  kiosk_up=0
  for i in $(seq 1 60); do # up to ~5 min each
    if curl -fsS -o /dev/null --max-time 4 "http://localhost:$port/" 2>/dev/null; then
      echo "  serving after ~$((i * 5))s"
      kiosk_up=1
      break
    fi
    sleep 5
  done
  if [ "$kiosk_up" != "1" ]; then
    echo "::error::nothing served on :$port"
    exit 1
  fi
done

# The gateway origin is baked into the served app (VITE_API_GATEWAY_URL). Poll
# the gateway -> camera-catalog route until 401 (service + auth up, not 5xx).
echo "Waiting for the gateway to route to camera-catalog ..."
ready=0
for i in $(seq 1 90); do # up to ~7.5 min more
  gw="$(curl -fsS --max-time 4 "$WEB/@fs$WS/apps/shared/src/api/gateway.ts" 2>/dev/null \
    | grep -oE 'http://localhost:[0-9]+' | head -1 || true)"
  if [ -n "$gw" ]; then
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 4 "$gw/camera-catalog/cameras" || true)"
    echo "  gateway=$gw camera-catalog=$code"
    if [ "$code" = "401" ]; then
      ready=1
      break
    fi
  fi
  sleep 5
done

if [ "$ready" = "1" ]; then
  echo "Stack ready (gateway probe confirmed)."
else
  echo "Gateway probe inconclusive; proceeding after a short buffer (Playwright CI retries absorb residual warm-up)."
  sleep 15
fi
