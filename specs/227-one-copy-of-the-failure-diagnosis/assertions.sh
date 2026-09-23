#!/bin/sh
# Every statement containing ".Should", one per line, whitespace-collapsed,
# whole-line comments removed. No normalisation: spec 227 edits no assertion.
for f in "$@"; do
  sed -E '/^[[:space:]]*\/\//d' "$f" \
  | sed -E 's/;[[:space:]]*$/;\x01/' \
  | tr '\n' ' ' \
  | tr '\001' '\n' \
  | grep -F '.Should' \
  | sed -E 's/[[:space:]]+/ /g; s/^ //; s/ $//'
done
