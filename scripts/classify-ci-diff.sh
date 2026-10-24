#!/usr/bin/env bash
# Sorts a CI diff into a bucket for the `changes` job in ci.yml (#3122, spec 448).
#
#   bucket=none     every path is under specs/        -> no job runs
#   bucket=backend  every path is backend-readable    -> only `backend` runs
#   bucket=full     anything else, or an unreadable diff -> everything runs
#
# Usage: classify-ci-diff.sh <event_name> <base_sha> <head_sha>
# Env:   GITHUB_OUTPUT, GITHUB_STEP_SUMMARY (files, appended to)
#
# Backend-readable paths are read at test time only by Architecture.Tests
# (AgentBriefClaimTests, OverlayFrameClaimTests, LatencyLegRecordTests,
# FoundingDecisionRecordTests, KioskRecoveryRecordTests), which run in the
# `backend` job. No frontend, integration or e2e test reads them. A new guard
# that reads one of these roots from outside the backend job must remove that
# root from this list. This is the only copy of the list.
#
# `bucket=full` is written first so that any early exit fails safe: GitHub
# keeps the last value written for a key. Not `set -e`: every failure needs
# its own explicit full-bucket exit, and the step must exit 0 on all of them.
set -uo pipefail

event_name="${1:-}"
base="${2:-}"
head="${3:-}"

echo "bucket=full" >> "$GITHUB_OUTPUT"

conclude() {
  echo "$1"
  echo "**changes:** $1" >> "$GITHUB_STEP_SUMMARY"
}

if [ -z "$base" ] || [ "$base" = "0000000000000000000000000000000000000000" ]; then
  conclude "bucket=full: no usable base to diff against (event=$event_name, base='$base') -- running everything."
  exit 0
fi

if ! git cat-file -e "${base}^{commit}" 2>/dev/null; then
  conclude "bucket=full: base commit $base not reachable -- running everything."
  exit 0
fi

# --no-renames prints both the old and new path of a move, so
# `git mv src/Foo.cs specs/Foo.cs` still counts its source path.
files="$(git diff --no-renames --name-only "$base" "$head")" || {
  conclude "bucket=full: git diff failed -- running everything."
  exit 0
}

if [ -z "$files" ]; then
  conclude "bucket=full: empty diff against base -- running everything."
  exit 0
fi

echo "Changed files (vs $base):"
echo "$files"

other=false
readable=false
while IFS= read -r f; do
  [ -z "$f" ] && continue
  case "$f" in
    specs/*) ;;
    docs/adr/* | docs/runbooks/* | docs/design/* | .specify/memory/* | .claude/* | CLAUDE.md) readable=true ;;
    *) other=true ;;
  esac
done <<< "$files"

if [ "$other" = "true" ]; then
  conclude "bucket=full: a path outside specs/ and the backend-readable roots changed -- running everything."
elif [ "$readable" = "true" ]; then
  conclude "bucket=backend: only backend-readable paths changed (with or without specs/) -- running backend only."
  echo "bucket=backend" >> "$GITHUB_OUTPUT"
else
  conclude "bucket=none: every changed path is under specs/ -- skipping the pipeline."
  echo "bucket=none" >> "$GITHUB_OUTPUT"
fi
exit 0
