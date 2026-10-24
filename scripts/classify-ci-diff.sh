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
# Backend-readable roots: docs/adr, .specify/memory, .claude and CLAUDE.md are
# read at test time by Architecture.Tests guards (AgentBriefClaimTests,
# OverlayFrameClaimTests, LatencyLegRecordTests, FoundingDecisionRecordTests,
# KioskRecoveryRecordTests), which run in the `backend` job. Nothing reads
# docs/runbooks or docs/design; they sit in the backend bucket so a future guard
# that reads them is not bypassed. No frontend, integration or e2e test reads any
# of these roots. A new guard that reads one of them from outside the backend job
# must remove that root from the `case` below.
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
# `git mv src/Foo.cs specs/Foo.cs` still counts its source path. -z and
# core.quotepath=false give the real path of a non-ASCII name instead of git's
# quoted octal form, which would never match a root below.
diff_file="$(mktemp)" || {
  conclude "bucket=full: could not create a temp file -- running everything."
  exit 0
}
trap 'rm -f "$diff_file"' EXIT

git -c core.quotepath=false diff --no-renames -z --name-only "$base" "$head" > "$diff_file" || {
  conclude "bucket=full: git diff failed -- running everything."
  exit 0
}

if [ ! -s "$diff_file" ]; then
  conclude "bucket=full: empty diff against base -- running everything."
  exit 0
fi

echo "Changed files (vs $base):"
tr '\0' '\n' < "$diff_file"

other=false
readable=false
while IFS= read -r -d '' f; do
  case "$f" in
    specs/*) ;;
    docs/adr/* | docs/runbooks/* | docs/design/* | .specify/memory/* | .claude/* | CLAUDE.md) readable=true ;;
    *) other=true ;;
  esac
done < "$diff_file"

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
