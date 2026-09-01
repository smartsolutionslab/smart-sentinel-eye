# Quickstart — 056 label over live video

How to run what this feature builds, and how to tell a real failure from an
environmental one.

---

## Run the fixture

```sh
npx playwright test --project=wall
```

The seed brings up a camera pointed at the fixture's video source, a variable,
an overlay bound to it, and a one-tile published wall. The teardown removes
them — **including on a partial run**, which a prior spec had to fix once
already.

## Run the measurement

```sh
npx playwright test wall-label-span
```

Five timed iterations. Prints every figure, the median, the range, the
conditions, and the legs the span covers.

---

## Reading the result

### The span

```
span: 5 runs — 214 / 231 / 238 / 244 / 402 ms   median 238   range 214-402
covers:     event → overlay state, overlay composite + render
NOT covered: camera → SFU, SFU → decode, presentation buffer
includes the label hold (ADR-0129): yes
```

**Read the range before the median.** A tight cluster means the system under
test is the bottleneck and the figure reproduces. A wide one means the machine
is, and it does not — that distinction is what made an earlier "~3x" claim
wrong, and it is the reason all five figures are printed rather than summarised.

**Do not compare the median to 800 ms without the third line.** Three legs are
absent from this span. A figure of 238 ms says nothing about whether the budget
holds.

### A refusal is a result

```
span: UNMEASURED — could not establish that both stamps share one clock
                   (browser is remote; only the test process's clock is read)
```

This is a **passing** outcome, not a failure. The alternative on offer is a
number assembled from parts, and that number would be a fabrication wearing a
measurement's clothes.

---

## When it fails, which failure is it?

| Symptom | Likely cause | What to check |
|---|---|---|
| `no frames decoded within 30s` | the source is not serving, or the SFU could not pull it | the video source container is up; the camera's registered URL resolves on the container network |
| `frames decoded: 3 in 1000ms (need 10)` | the stream stalled after starting | the FFmpeg loop; a clip that ran out and did not restart |
| `label text not found` | overlay unbound, unpublished, or the variable has no value | the overlay is Published and its binding resolves |
| `label did not follow the variable` | the change never reached the kiosk | the live-update transport, not the video path |
| `WHEP returned 404` | the SFU has no path for this camera | **this is the old failure mode** — the camera is pointed at an address nothing serves |

That last row is worth knowing by sight: it is what **every** overlay fixture in
this repository did before this feature, and it is why a label-only assertion
passed while no video existed.

---

## Adding a check to this wall

Assert **both halves**, always. A check that looks only at the label passes on
a wall with no video, which is the state this fixture exists to make
impossible. `C1` in the contract is the rule; the three existing specs are the
pattern.

---

## What you still cannot learn from any of this

- **Whether a wall looks right to a person.** Nothing automated establishes
  that someone has watched one align.
- **What a fab kiosk does.** These figures come from a CI runner or a
  developer machine. The conditions are printed so nobody has to guess which.
- **Whether the 800 ms budget holds.** See the third line of the output.
