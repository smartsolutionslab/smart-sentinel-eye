# AppHost dev resources

## `clips/` — Scenario Simulator loop clips (ADR-0111, spec 044, dev-only)

`camera-sim` (the second, config-clean MediaMTX) bind-mounts this **directory**
at `/media` and loops one clip per path via the per-path `runOnDemand` FFmpeg
hook the `scenario-simulator` worker provisions. Each scenario asset names the
clip it plays (`Camera.Clip` in `Scenarios/*.json`); a camera belonging to no
asset gets `sim-loop.mp4` with its name drawn on it.

All **dev-only** — never used by CI/E2E/prod, because the camera-sim container
and the worker are both gated `isRunMode && !isE2ETests` in `AppHost.cs`.

The host has no FFmpeg, so clips are generated from the MediaMTX
`latest-ffmpeg` image. Run once and commit the results:

```bash
bash scripts/generate-sim-clips.sh              # all clips
bash scripts/generate-sim-clips.sh electronics- # only names matching a prefix
```

Each writes a 1280x720, ~20 s, H.264 baseline, 25 fps excerpt. Two are shorter
than 20 s because their sources are (`electronics-smd-line` 16.5 s,
`electronics-conveyor` 18 s); they loop regardless.

If the directory is missing, `aspire run` fails the `camera-sim` bind mount;
generate it first.

### Licensing

**Every clip has a matching `<name>.ATTRIBUTION.txt` beside it, and must.** The
eight spec-044 clips come from Wikimedia Commons' *Sounds of Changes* project
under **CC BY 3.0** (attribution only). `sim-loop.mp4` is older and is
**CC BY-SA 3.0** — share-alike — so it is the one with the stricter terms.

Take a licence from the Commons **file page**, never from the `extmetadata`
API: they disagree, and the page is authoritative.
