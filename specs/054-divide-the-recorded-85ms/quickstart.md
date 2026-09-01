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

## 3. Find the endpoints — and settle Keycloak by asking, not by rule

Ports are assigned per boot: every service uses `.WithHttpEndpoint()` with no
port, so they change each time and the driver has to be told.

**For `system-variables`**, take the **http** endpoint. The dashboard shows both;
the driver accepts either, but https on a host without a trusted dev certificate
fails inside the drive rather than at startup.

**For Keycloak, do not follow a rule — ask the realm which address it issues
tokens for:**

```sh
curl -sk https://<candidate>/realms/smart-sentinel-eye/.well-known/openid-configuration \
  | grep -o '"issuer":"[^"]*"'
```

**Use whatever that prints.** A token minted against an address the realm does not
claim as its issuer is rejected by every service, everything 401s, and nothing in
the failure names the cause.

> **Why this is a question rather than an instruction.** The standing advice in
> this repository is to prefer Aspire's proxied endpoint over the container's
> mapped port, and that advice is right when the two differ. On a stack whose
> Keycloak is a *persistent* container with a fixed port, the issuer **is** the
> container address — spec 054's own measurement used `https://localhost:10756`
> for exactly that reason. Following the rule blindly there would produce the 401
> the rule exists to prevent. The `issuer` field settles it in one command; a rule
> cannot.

**For the audit database**, any connection string reaching run mode's `audit-db`.

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
