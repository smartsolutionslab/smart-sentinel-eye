using Aspire.Hosting.ApplicationModel;

namespace SmartSentinelEye.AppHost;

// PLACEHOLDER for phase 4b (#2268, `infra-engineer`) — exists only so
// AppHostStackStatusTests.cs (tests/Integration.Tests) compiles and can
// genuinely execute and fail (ADR-0139), rather than failing the whole
// Integration.Tests assembly to build over a type that does not exist yet.
//
// This is deliberately NOT plan.md §2.2's writer: no seeding from the
// DistributedApplicationModel, no immediate write, no watch, no atomic
// File.Move. It throws unconditionally so it cannot accidentally satisfy the
// test it exists only to let compile. Phase 4b replaces this file's
// contents — it does not extend them — and picks the real shape (plan.md
// §2.2/§2.7 names two candidate designs and leaves the choice to phase 4).
public static class StackStatusReport
{
    /// <summary>
    /// Will become the resource set the writer seeds into the report: every
    /// resource in <paramref name="resources"/> that is not
    /// <see cref="IResourceWithoutLifetime"/> (plan.md §2.2 step 1).
    /// </summary>
    public static IReadOnlyList<string> ExpectedResourceNames(IEnumerable<IResource> resources) =>
        throw new NotImplementedException("phase 4b (#2268) implements this — see plan.md §2.2/§2.7");
}
