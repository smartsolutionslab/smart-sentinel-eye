/**
 * Spec 316 FR-013 — rendered inside the shell's layout for a route whose
 * entry is not in the session's visible set. Navigation visibility is a UX
 * affordance, not a trust boundary (spec.md §User Story 2): the endpoint's
 * own `RequireScope` check stays the authority.
 */
export function NotAvailable() {
  return (
    <section className="mx-auto mt-16 flex max-w-lg flex-col items-center gap-4 p-8 text-center">
      <h1 className="text-2xl font-semibold">Not available to your account</h1>
      <p className="text-fg-muted">You do not have access to this surface.</p>
    </section>
  );
}
