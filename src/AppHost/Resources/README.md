# AppHost dev resources

## `sim-loop.mp4` — Scenario Simulator loop clip (ADR-0111, dev-only)

`camera-sim` (the second, config-clean MediaMTX) bind-mounts this clip at
`/media/sim-loop.mp4` and loops it via the per-path `runOnDemand` FFmpeg hook
the `scenario-simulator` worker provisions. It is **dev-only** — never used by
CI/E2E/prod (the camera-sim container and the worker are both gated
`isRunMode && !isE2ETests` in `AppHost.cs`).

The host has no FFmpeg, so the clip is generated from the MediaMTX
`latest-ffmpeg` image. Run once and commit the result:

```bash
bash scripts/generate-sim-loop.sh
```

This writes `src/AppHost/Resources/sim-loop.mp4`: 1280x720, 20 s, H.264
baseline, 25 fps — a `testsrc2` moving pattern (with a built-in counter) plus a
blue box that scrolls and wraps every 20 s for a seamless loop.

If the file is missing, `aspire run` fails the `camera-sim` bind mount; generate
it first.
