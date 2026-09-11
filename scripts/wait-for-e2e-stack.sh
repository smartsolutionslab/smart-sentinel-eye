#!/usr/bin/env bash
# Waits for the Aspire RUN-mode stack to be ready for the Playwright e2e suite
# (ADR-0108): management-web serving on :5173, and the gateway routing to a
# context service (so OIDC + gateway + services are warm). Best-effort on the
# gateway probe — if it can't be read it falls back to a buffer and lets
# Playwright's CI retries absorb any residual warm-up.
set -uo pipefail

WEB="http://localhost:5173"
WS="${GITHUB_WORKSPACE:-$PWD}"

# The migration run, first. It is the cheapest question and the one whose
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
