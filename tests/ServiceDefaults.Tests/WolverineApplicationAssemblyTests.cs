using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Issue #2188 / spec 139. <c>TryLoadApplicationAssembly</c> currently collapses
/// three distinct <c>Assembly.Load</c> failures into the same silent <c>null</c>:
/// the legitimate absence (<see cref="FileNotFoundException"/>) and two cases
/// where the file exists but will not load (<see cref="FileLoadException"/>,
/// <see cref="BadImageFormatException"/>). Only the first is the documented
/// contract; the other two mean a bounded context's handlers silently never
/// register while the service reports healthy.
///
/// <para>
/// These tests trigger the CLR's own loader exceptions via
/// <see cref="AssemblyLoadContext.Resolving"/> rather than mocking them —
/// nothing about <c>Assembly.Load</c> is mockable, and the point is to prove the
/// real exception types propagate correctly.
/// </para>
///
/// <para>
/// <b>Phase 4a (ADR-0144), behaviour-changing, red first.</b> The two
/// "fails the host" tests must be observed failing before phase 4b, and the two
/// "is not an error" tests are characterisation of the branch this change must
/// not touch — each test's colour is stated in its own doc comment so a
/// reviewer does not mistake a green test here for evidence the fix already
/// landed.
/// </para>
///
/// <para>
/// This file will not compile until phase 4b widens
/// <c>TryLoadApplicationAssembly</c> from <c>private static</c> to
/// <c>internal static</c> and adds the matching <c>InternalsVisibleTo</c> entry
/// to <c>SmartSentinelEye.ServiceDefaults.csproj</c> (plan.md, "Access"). That
/// accessibility error is itself the correct state to hand this phase off in.
/// </para>
/// </summary>
public sealed class WolverineApplicationAssemblyTests
{
    /// <summary>
    /// GREEN — characterisation of the preserved branch (FR-001). No
    /// <see cref="AssemblyLoadContext.Resolving"/> handler is registered for
    /// this probe's <c>.Application</c> name at all, so default probing finds
    /// nothing and <c>Assembly.Load</c> raises <see cref="FileNotFoundException"/> —
    /// the one legitimate absence: "this context has no Application handlers to
    /// discover". This test must stay green before and after phase 4b; an
    /// assertion that needs editing afterward would mean the fix over-reached
    /// into the absent case.
    /// </summary>
    [Fact]
    public void An_absent_application_assembly_is_not_an_error()
    {
        Assembly infrastructureAssembly = CreateDynamicAssembly($"Probe{Guid.NewGuid():N}.Infrastructure");

        Assembly? result = Should.NotThrow(() => WolverineDefaults.TryLoadApplicationAssembly(infrastructureAssembly));

        result.ShouldBeNull();
    }

    /// <summary>
    /// GREEN — characterisation of the preserved branch (FR-002). An assembly
    /// whose simple name does not end in <c>.Infrastructure</c> is rejected by
    /// the naming check before any load is attempted, so the resolving handler
    /// — which would otherwise answer for this probe's derived name — must
    /// never be invoked. This test must stay green before and after phase 4b.
    /// </summary>
    [Fact]
    public void An_assembly_outside_the_naming_convention_is_not_probed()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using ResolvingProbe probe = new($"Probe{suffix}.Application", _ => null);

        Assembly infrastructureAssembly = CreateDynamicAssembly($"Probe{suffix}.SomethingElse");

        Assembly? result = Should.NotThrow(() => WolverineDefaults.TryLoadApplicationAssembly(infrastructureAssembly));

        result.ShouldBeNull();
        probe.InvocationCount.ShouldBe(0);
    }

    /// <summary>
    /// RED — must be observed failing until phase 4b lands (FR-003, FR-004).
    /// The resolving handler hands <c>Assembly.Load</c> genuinely corrupt bytes;
    /// the CLR itself rejects them with <see cref="BadImageFormatException"/>.
    /// Today that exception is caught and silently turned into <c>null</c> — the
    /// assembly is present but broken, and the service would start healthy with
    /// zero handlers registered for it. Expected failure before the fix:
    /// "expected System.InvalidOperationException but no exception was thrown".
    /// </summary>
    [Fact]
    public void A_corrupt_application_assembly_fails_the_host()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string applicationName = $"Probe{suffix}.Application";
        using ResolvingProbe probe = new(applicationName, _ => Assembly.Load([0x01, 0x02, 0x03, 0x04]));

        Assembly infrastructureAssembly = CreateDynamicAssembly($"Probe{suffix}.Infrastructure");

        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => WolverineDefaults.TryLoadApplicationAssembly(infrastructureAssembly));

        exception.Message.ShouldContain(applicationName);
        exception.InnerException.ShouldBeOfType<BadImageFormatException>();
    }

    /// <summary>
    /// RED — must be observed failing until phase 4b lands (FR-003, FR-004).
    /// The resolving handler hands back an assembly whose identity does not
    /// match the request (<see cref="object"/>'s own assembly); the *runtime
    /// itself* then raises <see cref="FileLoadException"/> — this is the "found
    /// but will not load" case, produced by the loader rather than thrown by the
    /// test. Today it is caught and silently turned into <c>null</c>. Expected
    /// failure before the fix: "expected System.InvalidOperationException but no
    /// exception was thrown".
    /// </summary>
    [Fact]
    public void An_application_assembly_that_is_present_but_unloadable_fails_the_host()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string applicationName = $"Probe{suffix}.Application";
        using ResolvingProbe probe = new(applicationName, _ => typeof(object).Assembly);

        Assembly infrastructureAssembly = CreateDynamicAssembly($"Probe{suffix}.Infrastructure");

        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => WolverineDefaults.TryLoadApplicationAssembly(infrastructureAssembly));

        exception.Message.ShouldContain(applicationName);
        exception.InnerException.ShouldBeOfType<FileLoadException>();
    }

    /// <summary>
    /// A dynamic, in-memory assembly gives <c>TryLoadApplicationAssembly</c> a
    /// simple name to rewrite without anything on disk — the method only ever
    /// reads <c>GetName().Name</c> off it.
    /// </summary>
    private static AssemblyBuilder CreateDynamicAssembly(string simpleName) =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(simpleName), AssemblyBuilderAccess.Run);

    /// <summary>
    /// Subscribes to <see cref="AssemblyLoadContext.Resolving"/> so a test can
    /// make <c>Assembly.Load</c> genuinely raise the CLR's own loader
    /// exceptions instead of mocking them.
    ///
    /// <para>
    /// <see cref="AssemblyLoadContext.Resolving"/> is process-global and xUnit
    /// runs test classes in parallel, so every probe answers only the one
    /// <c>.Application</c> name it was built for (a name unique per test via
    /// <see cref="Guid.NewGuid()"/> at the call site) and returns <c>null</c>
    /// for every other request — a handler that answered broadly would corrupt
    /// unrelated tests running at the same time. The subscription is removed in
    /// <see cref="Dispose"/> so it can never outlive the test that installed it.
    /// </para>
    /// </summary>
    private sealed class ResolvingProbe : IDisposable
    {
        private readonly string applicationAssemblyName;
        private readonly Func<AssemblyName, Assembly?> resolve;

        public ResolvingProbe(string applicationAssemblyName, Func<AssemblyName, Assembly?> resolve)
        {
            this.applicationAssemblyName = applicationAssemblyName;
            this.resolve = resolve;
            AssemblyLoadContext.Default.Resolving += Handle;
        }

        public int InvocationCount { get; private set; }

        public void Dispose() => AssemblyLoadContext.Default.Resolving -= Handle;

        private Assembly? Handle(AssemblyLoadContext context, AssemblyName requested)
        {
            if (requested.Name != applicationAssemblyName)
            {
                return null;
            }

            InvocationCount++;
            return resolve(requested);
        }
    }
}
