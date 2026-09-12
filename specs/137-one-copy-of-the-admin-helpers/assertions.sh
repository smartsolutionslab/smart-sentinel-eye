#!/bin/sh
for f in "$@"; do
  sed -E '/^[[:space:]]*\/\//d' "$f" \
  | sed -E 's/;[[:space:]]*$/;\x01/' \
  | tr '\n' ' ' \
  | tr '\001' '\n' \
  | grep -F '.Should' \
  | sed -E 's/RealmProbe\.//g; s/[[:space:]]+/ /g; s/^ //; s/ $//'
done
