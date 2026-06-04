#!/usr/bin/env bash
# Generates the dev-only seamless-loop clip for the Scenario Simulator
# (ADR-0111). ffmpeg is absent on the host, so we run it from the MediaMTX
# `latest-ffmpeg` image. Output: src/AppHost/Resources/sim-loop.mp4.
#
# 1280x720, 20 s, H.264 baseline yuv420p, 25 fps. `testsrc2` gives a moving
# test pattern with a built-in counter (a clear "this is playing" readout that
# needs no external font); a blue box scrolls across and wraps every 20 s so
# the clip loops seamlessly.
#
# Run once and commit the result:
#   bash scripts/generate-sim-loop.sh
set -euo pipefail

# `pwd -W` yields a Windows path on Git Bash (which Docker Desktop needs for the
# bind mount); it falls back to a normal POSIX `pwd` on Linux/macOS.
RES_DIR="$(cd "$(dirname "$0")/../src/AppHost/Resources" && { pwd -W 2>/dev/null || pwd; })"

VF="drawbox=x='mod(t*64,1480)-200':y=300:w=200:h=120:color=0x33aaff@1:t=fill"

# MSYS_NO_PATHCONV stops Git Bash on Windows rewriting the container path `/out`
# (it would otherwise become `C:/Program Files/Git/out`); harmless elsewhere.
MSYS_NO_PATHCONV=1 docker run --rm -v "${RES_DIR}:/out" --entrypoint ffmpeg bluenviron/mediamtx:latest-ffmpeg \
  -f lavfi -i "testsrc2=s=1280x720:r=25:d=20" \
  -vf "${VF}" \
  -c:v libx264 -profile:v baseline -pix_fmt yuv420p -g 25 -r 25 -t 20 \
  -movflags +faststart -y /out/sim-loop.mp4

echo "Wrote ${RES_DIR}/sim-loop.mp4"
