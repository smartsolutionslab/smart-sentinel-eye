#!/usr/bin/env bash
# Generates the dev-only seamless-loop clip for the Scenario Simulator
# (ADR-0111): a ~20 s 1280x720 H.264 excerpt of a real hot-rolling mill, which
# camera-sim loops per catalog camera via its runOnDemand FFmpeg. Output:
# src/AppHost/Resources/sim-loop.mp4.
#
# Source footage:
#   "Proizvodnja vroče valjane debele pločevine v Acroni SIJ Jesenice"
#   Author: MZaplotnik — Wikimedia Commons — CC BY-SA 3.0
#   https://commons.wikimedia.org/wiki/File:Proizvodnja_vro%C4%8De_valjane_debele_plo%C4%8Devine_v_Acroni_SIJ_Jesenice.ogv
# The committed sim-loop.mp4 is a 20 s excerpt (a hot-plate rolling pass) and is
# therefore ALSO CC BY-SA 3.0 — see src/AppHost/Resources/sim-loop.ATTRIBUTION.txt.
#
# ffmpeg is absent on the host, so we run it from the MediaMTX `latest-ffmpeg`
# image. Re-run to regenerate, then commit the result:
#   bash scripts/generate-sim-loop.sh
set -euo pipefail

SRC_URL="https://upload.wikimedia.org/wikipedia/commons/b/b9/Proizvodnja_vro%C4%8De_valjane_debele_plo%C4%8Devine_v_Acroni_SIJ_Jesenice.ogv"
SEGMENT_START=250   # seconds into the source — a hot-plate rolling pass (glowing steel)
SEGMENT_LEN=20

# `pwd -W` yields a Windows path on Git Bash (which Docker Desktop needs for the
# bind mount); it falls back to a normal POSIX `pwd` on Linux/macOS.
RES_DIR="$(cd "$(dirname "$0")/../src/AppHost/Resources" && { pwd -W 2>/dev/null || pwd; })"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
echo "Downloading source footage (~118 MB, CC BY-SA 3.0 by MZaplotnik)..."
curl -fsSL --max-time 600 -o "$WORK/source.ogv" "$SRC_URL"
WORK_DIR="$(cd "$WORK" && { pwd -W 2>/dev/null || pwd; })"

# MSYS_NO_PATHCONV stops Git Bash on Windows rewriting the container paths
# (`/work`, `/out`) into `C:/Program Files/Git/...`; harmless elsewhere. Input
# seeking on the local file is exact; the source is already 1280x720.
MSYS_NO_PATHCONV=1 docker run --rm -v "${WORK_DIR}:/work" -v "${RES_DIR}:/out" \
  --entrypoint ffmpeg bluenviron/mediamtx:latest-ffmpeg \
  -ss "${SEGMENT_START}" -i /work/source.ogv -t "${SEGMENT_LEN}" \
  -vf "scale=1280:720,setsar=1,fps=25" \
  -c:v libx264 -profile:v baseline -level:v 3.1 -pix_fmt yuv420p -g 25 -an \
  -movflags +faststart -y /out/sim-loop.mp4

echo "Wrote ${RES_DIR}/sim-loop.mp4"
