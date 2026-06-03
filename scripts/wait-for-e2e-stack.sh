#!/usr/bin/env bash
# Waits for the Aspire RUN-mode stack to be ready for the Playwright e2e suite
# (ADR-0108): management-web serving on :5173, and the gateway routing to a
# context service (so OIDC + gateway + services are warm). Best-effort on the
# gateway probe — if it can't be read it falls back to a buffer and lets
# Playwright's CI retries absorb any residual warm-up.
set -uo pipefail

WEB="http://localhost:5173"
WS="${GITHUB_WORKSPACE:-$PWD}"

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
