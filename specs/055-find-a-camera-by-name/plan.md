# Implementation Plan: Find a camera by name

**Branch**: `055-find-a-camera-by-name` | **Date**: 2026-09-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/055-find-a-camera-by-name/spec.md`

## Summary

An operator who knows a camera's name cannot reach it unless they know how the
name *begins*. This adds a name fragment to the camera list query, matched
anywhere in the name, and a filter field on the two screens where cameras are
chosen.

**Two of the three decisions here make the feature smaller than it looks**: the
count discipline US2 needs is already implemented and commented in the handler, so
the filter inherits it by going where the others go; and the picker's native
`<select>` supplies US3's keyboard and screen-reader behaviour for free, so a
filter *beside* it costs far less than a combobox that replaces it.

## Technical Context

**Language/Version**: C# / .NET 10, TypeScript / React 19

**Primary Dependencies**: EF Core, RTK Query. **No new package** — see research §4.

**Storage**: PostgreSQL. Matching reuses the existing generated `name_normalized`
column; **no migration**.

**Testing**: xUnit for the query handler, Vitest + Testing Library for the screens,
Playwright for the operator path.

**Target Platform**: management-web, both the layout editor's picker and the
cameras list page.

**Project Type**: web feature — one query field, one UI control, two screens.

**Performance Goals**: none set. FR-014 requires the filtered query be *measured*
at 250 cameras per fab and the figure recorded, whichever way it falls.

**Constraints**: the existing list contract is extended, not replaced — a caller
sending no fragment sees exactly today's behaviour.

**Scale/Scope**: 250 cameras per fab (constitution §Scale).

## Constitution Check

*GATE: must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| §IV latency budget | **Not on the event-to-overlay path.** A catalogue query on an operator screen. No leg touched, no figure changed. |
| DDD / value objects | The fragment is operator input, validated at the trust boundary and never a domain concept. No primitive crosses a domain boundary. |
| No cross-context references | CameraCatalog only. |
| Minimal APIs (ADR-0070) | A query parameter on an existing endpoint. |
| Radix headless + own design system (ADR-0077) | Respected, and **no new primitive is needed** — research §4. |
| Coverage gates (ADR-0065) | **Live.** CameraCatalog's Application layer is touched, so ADR-0065's ≥80% Application threshold applies. Stated because two recent specs got this wrong in opposite directions. |
| Rebase-only, Conventional Commits | Unchanged. |

**Locked decisions checked — ADR-0070, 0077, 0043/0113, 0091/0094 — and there is
no conflict.** See research §0.

## Project Structure

### Documentation (this feature)

```text
specs/055-find-a-camera-by-name/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── contracts/
│   └── the-filter.md
├── quickstart.md
└── checklists/
    └── requirements.md
```

### Source (this feature)

```text
src/CameraCatalog/
├── Application/Queries/ListCamerasQuery.cs            # + NameFragment
├── Application/Queries/Handlers/ListCamerasQueryHandler.cs  # filter before the count
└── Api/CameraEndpoints.cs                             # + ?name=

apps/shared/src/api/
└── cameras.api.ts                                     # fragment through the client

apps/management-web/src/features/
├── layouts/GridDesigner.tsx                           # filter field beside the select
└── cameras/CamerasPage.tsx                            # filter field on the list
```

## Design

### Where the filter goes, and why that is the whole of US2

`ListCamerasQueryHandler` already filters `visible` twice before `CountAsync`, and
comments both times that the total must describe what the caller can page through.
**The name filter joins those two.** The count then describes the matches because
it is computed from the same query — not because anyone remembered to make it.

Anywhere else — the endpoint, after `Skip`/`Take`, or in the browser over a page
it already holds — produces a total that describes a different population from the
items beside it. That is the defect class already filed against consumers
rendering one page as the whole list, and this feature would be **creating** an
instance rather than finding one.

### What "matches" means

Case-insensitive substring, against the existing generated `name_normalized`
(`upper(name)`), trimmed of surrounding whitespace. **Accents do not fold.**

That is not three separate choices: it is one — *reuse the normalisation the
uniqueness constraint already uses* — and the rest follows. Search and uniqueness
then agree about when two names are the same name. Research §2–§3 carries the
reasoning; **FR-004 requires it be written where an operator will find it**, which
means the record, not only this plan.

### One rule, on the server

The picker could filter in memory — it already holds every camera — and would feel
instant. **Rejected**: that is a second implementation of "matches", in a second
language, and an operator cannot tell which one they hit. The cost is a round trip
per filter change, and §Performance below is how we find out whether that matters.

### A filter field, not a combobox

The picker is a native `<select>`. It already provides role and value
announcement, arrow-key movement, Escape, and the start-of-name type-ahead FR-012
must preserve. **A filter field beside it keeps all of that and adds the missing
capability.** A combobox would re-implement it, from scratch, with Radix shipping
none to build on.

The cost is two controls where a combobox is one. The plan owes: how they are
associated for assistive technology, and how the match count is announced when it
changes.

### Performance

One measurement, at 250 cameras in one fab: the filtered query's time, recorded
whichever way it falls. **No index is added unless that measurement asks for one**
— the existing btree cannot serve a substring match, and a trigram index would
mean an extension and a migration to keep true.

## "Done", per user story, before any code

| Story | Done when | What this does **not** prove |
|---|---|---|
| **US1** find it | A camera whose distinguishing word is not first is returned by a fragment of it, case-insensitively, trimmed | That the operator's remembered name matches the catalogue's — see research §7 |
| **US2** honest total | A filtered response's total equals the number of matches, asserted at the handler **and** through the endpoint, with paging across a multi-page match set | That every future consumer reads it — only that it is true |
| **US3** keyboard | The whole task completes with no pointer, and the control announces role, value and match count | That it is *pleasant* to use that way; only that it is possible |

**The check that cannot exist**: that an operator finds the camera they meant.
What the feature owes instead is FR-009 — that a miss is plainly a miss, and not
mistakable for a list still loading.

## Three things most likely to go wrong

1. **The filter is applied where the count cannot see it.** The tempting places —
   the endpoint, the client, after paging — all produce a total that describes a
   different population than the rows. The handler's existing comments are the
   guard; the risk is not reading them.

2. **The combobox gets built anyway.** It is what the issue's title suggests and
   what "search" implies. It is also a new accessibility contract, written from
   scratch, replacing one that works. If it turns out to be needed, that is a
   finding to record — not a default to drift into.

3. **Two match rules appear.** A client-side filter added "just for the picker,
   just for responsiveness" is exactly how the second rule arrives, and it will
   agree with the first on the day it is written.

## Phase ordering

1. **The query** — fragment on the query, filter before the count, handler tests.
2. **The contract** — endpoint parameter, shared client, unchanged behaviour when absent.
3. **The screens** — filter field on the picker, then the list page.
4. **The record** — the match rule where an operator finds it, and the measurement.

Step 1 first because US2 is settled there or nowhere.
