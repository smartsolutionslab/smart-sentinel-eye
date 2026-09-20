#!/bin/sh
# Source text with every line comment removed and whitespace collapsed.
# Hashing this proves a change is comment-only, or that code is untouched.
# Line comments only: these files contain no /* */ block comments (verified).
for f in "$@"; do
  sed -E 's://.*$::' "$f" \
  | sed -E 's/[[:space:]]+/ /g; s/^ //; s/ $//' \
  | grep -v '^$'
done
