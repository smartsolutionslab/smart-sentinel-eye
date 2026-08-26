# Contract: what a credential carries, and where each part comes from

**Feature**: 042 | **Date**: 2026-08-26

No HTTP contract changes here. What *is* a contract is the shape of a credential
and which piece of configuration is responsible for each part of it — which is
currently answered three different ways, two of them by accident.

---

## 1. The three kinds of thing a scope can be

The realm already follows this rule and has never stated it. Stating it is half
the feature.

| Shape | Example | Grants | Appears in what the caller may do |
|---|---|---|---|
| `sse.<noun>.<verb>` | `sse.cameras.read` | a permission | yes |
| `sse-<noun>` | `sse-groups`, **`sse-identity`** | a claim | **no** |
| *(legacy)* | `sse.management` | every permission | yes |

A **claim carrier** sets `include.in.token.scope: false`, so it never appears
among what the caller may do — because it grants nothing. A permission that also
carries a claim is neither, and is what this feature removes.

---

## 2. Where each part of a credential comes from

| Part | Source, after this feature | Source before |
|---|---|---|
| who holds it (`sub`) | `sse-identity`, for every identity in the file | one permission, one private copy, or the grant type |
| which plants they work in (`groups`) | `sse-groups` | unchanged |
| what they may do (`scope`) | the `sse.*` permissions each identity holds | unchanged |
| a display name | **nothing** — removed | one permission, read by nobody |

**The middle column is the contract.** One row, one source, no exceptions inside
the file.

---

## 3. The exception outside the file, written down because it looks like luck

Identities that act for **no person** — background workers, and the kiosks and
devices enrolled at runtime — carry the holder's subject **because of how they
sign in**, not because of any configuration. The sign-in service fills it from
the account that exists for that purpose.

This is why they were never affected, why a check on the file cannot see them,
and why the first draft of the spec counted five of them as broken.

**It stops being true the moment such an identity acts on a person's behalf.**
Nothing enforces that, and nothing can: it is a property of the grant, not of the
realm.

---

## 4. What is refused, and why that is correct

A change that cannot be attributed is **refused**, in seventeen places. It is not
recorded against nobody, and not against a fabricated person — that would corrupt
the audit trail, and the code says so where it is enforced.

This feature removes the reason that refusal fires. It does not soften it, and
nothing here should be read as permission to.

---

## 5. What must fail

| Break this | Caught by | Not caught by |
|---|---|---|
| A client without the identity scope | the convention check | — |
| A client naming a scope that does not exist | the convention check | today: discarded with a warning |
| The identity scope's mapper removed or misconfigured | the **integration** test, by minting a token | the convention check — it reads names, not behaviour |
| A permission scope regaining an identity mapper | the convention check | — |
| A runtime-created identity that cannot be attributed | **nothing** | both — see §3 |

The last row is stated rather than solved. The file cannot describe clients that
are not in it, and pretending otherwise is the failure this feature exists to
correct.
