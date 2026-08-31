# Quickstart — taking the run-mode measurement

The runbook. It exists because the address trap in step 3 has cost this
repository time before, and because a measurement whose conditions live in
somebody's memory is not reproducible.

---

## 1. Stop anything already running

A leftover AppHost holds the service binaries and the build fails with MSB3027,
which looks like a broken build rather than a running stack.

```sh
# Windows
powershell -NoProfile -Command "Get-Process -Name 'SmartSentinelEye.AppHost' -ErrorAction SilentlyContinue | Stop-Process -Force"
```

Leave the **persistent containers** alone — run mode's Postgres holds the audit
history, and the run isolates its own rows anyway.

---

## 2. Start run mode with the measurement conditions set

Both variables must be set **in the shell that launches the AppHost**, because
they propagate from there into the child service processes. Setting them later, or
in a different shell, reaches nothing — which has already cost one wasted run.

```sh
export AuditObservability__Measurement__RecordIngestBreakdown=true
export Logging__LogLevel__Default=Warning
dotnet run --project src/AppHost
```

**Why `Warning` and not the default.** Development pins `Debug`, where this stack
sustains 60–83 ev/s — below the 100 ev/s the requirement names. A run there
measures the logging as much as the pipeline, and the driver refuses it.

---

## 3. Read the endpoints off the Aspire dashboard

Take the **proxied** addresses the dashboard shows for `system-variables`,
`keycloak` and the Postgres connection — **not** the ports `docker ps` reports.

> **This is the trap.** A token minted against the container's mapped port is
> rejected by every service, because the issuer in the token does not match the
> one the services expect. Everything 401s and the cause is not visible in the
> failure.

Ports are assigned per boot: every service uses `.WithHttpEndpoint()` with no
port, so these change each time. That is why the driver is told rather than
guessing.

---

## 4. Run the measurement

```sh
export SSE_RUNMODE_SYSTEM_VARIABLES=<system-variables address from the dashboard>
export SSE_RUNMODE_KEYCLOAK=<keycloak address from the dashboard>
export SSE_RUNMODE_AUDIT_DB=<audit-db connection string>

dotnet test tests/Integration.Tests --filter "FullyQualifiedName~RunModeIngestAttribution" \
  --logger "console;verbosity=detailed"
```

`verbosity=detailed` is required. Without it xUnit's output — which is the entire
result — is not rendered.

**With nothing configured the run refuses and names what it could not reach.** It
never boots a stack; a run labelled "run mode" that quietly measured something
else is the worst outcome available here.

---

## 5. Check the conditions block before believing the numbers

The run prints, before its assertions:

- the **address it actually connected to** — check this against the stack you
  started, because nothing automated can
- achieved rate beside intended
- logging level
- measurement-switch state
- rows measured and rows missing stamps

A second check that costs nothing: the audit store should have grown by exactly
the measured count.

---

## 6. Repeat, at least three times

One side of this measurement is noisy. At `Warning` the same configuration has
given 169.8, 173.7 and 244.4 ev/s — the bottleneck there is the machine, not the
logging, so the figure does not reproduce the way the Debug one does.

**Do not quote an effect size from a single pair of runs.** That is how an
overstated figure already reached this repository's record once.

---

## 7. Afterwards

Stop the AppHost. The persistent containers can stay — they are how run mode keeps
its history, and the next measurement isolates its own rows regardless.
