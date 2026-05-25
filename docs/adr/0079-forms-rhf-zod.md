# ADR-0079: Forms — React Hook Form + Zod

**Status:** Accepted
**Date:** 2026-05-25

## Context

The management app has many forms: camera registration, layout
designer, overlay editor, automation rule builder, system variable
config. Each needs typed input, client-side validation that mirrors
the backend, and good ergonomics.

## Decision

**React Hook Form (RHF) + Zod**.

- **Zod schemas** define input shapes and validation. The same schema
  is reused for:
  - form validation via `zodResolver`,
  - parsing API responses (the generated API client returns typed
    objects validated against the same shape),
  - shared types via `z.infer<typeof schema>`.

- **RHF** for uncontrolled-by-default forms (re-renders only the
  dirty field; strong TS inference; integrates with Radix primitives
  via `Controller` or `register`).

```typescript
const registerCameraSchema = z.object({
  name: z.string().min(1).max(200),
  rtspUrl: z.string().startsWith('rtsp://'),
  onvifProfile: z.string().optional(),
});

type RegisterCameraInput = z.infer<typeof registerCameraSchema>;

function RegisterCameraForm() {
  const form = useForm<RegisterCameraInput>({
    resolver: zodResolver(registerCameraSchema),
  });
  // ... render FormField composites from apps/shared/ui/composites/
}
```

- **One Zod schema per concept.** Lives next to the API client
  endpoint definition.
- **`FormField`** composite in `apps/shared/ui/composites/` wraps
  Radix Label + Input/Select + error message; integrates with RHF.

## Consequences

- **Positive:** one source of truth for input shapes across forms
  and HTTP.
- **Positive:** RHF re-renders only the dirty field — performant for
  large forms (layout designer).
- **Positive:** Zod's `parse` doubles as runtime validation at
  module boundaries.
- **Negative:** two libraries to learn. Trade-off accepted.

## Alternatives Considered

- **TanStack Form** — newer; smaller ecosystem.
- **Native HTML forms + Zod** — loses RHF's optimizations and
  field-array helpers.
- **Formik** — controlled-by-default; surpassed by RHF.
