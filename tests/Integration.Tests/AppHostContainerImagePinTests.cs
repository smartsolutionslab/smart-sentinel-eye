namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Guards that every container the composed application model actually pulls
/// from a registry resolves to a pinned reference (issue #2270, spec 187).
///
/// <para>
/// <c>Architecture.Tests.ContainerImagePinTests</c> reads <c>AppHost.cs</c> as
/// text and has no notion of call order: <c>WithImage(reference)</c> assigns
/// <c>"latest"</c> when given no tag, so a <c>WithImage</c> call that runs
/// <b>after</b> <c>WithImageTag</c> silently overwrites the pin while the
/// literal that source guard matched keeps sitting in the file, still passing,
/// describing a tag nothing resolves. This class reads the
/// <see cref="ContainerImageAnnotation"/> the composed model actually carries —
/// the same annotation <c>WithImage</c> writes — so the order the calls ran in
/// is already baked into what is inspected.
/// </para>
///
/// <para>
/// Also catches what the source scan cannot see at all: <c>keycloak</c>'s image
/// coordinates come from <c>Aspire.Hosting.Keycloak</c>, not from a literal in
/// this repository, so a package bump that floated its default tag would leave
/// no text for a source scan to match.
/// </para>
///
/// <para>
/// Composes the model exactly as <see cref="AppHostMediaMtxImageTests"/> does —
/// <c>CreateAsync</c> starts no resources, so this costs no container and is
/// safe beside a live stack. The two argument arrays are copied from that class
/// rather than shared: two classes with four literal parameter values each is
/// not yet an abstraction (ADR-0036), and a shared fixture would couple two
/// guards whose populations deliberately differ (this one includes
/// <c>keycloak</c> and <c>minio</c>, which that one has no reason to touch).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostContainerImagePinTests
{
    private static readonly string[] RunModeArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
    ];

    private static readonly string[] FixtureArguments =
    [
        .. RunModeArguments,
        "E2ETests=true",
    ];

    /// <summary>
    /// Run mode: the largest population, including the two resources
    /// (<c>fixture-video</c>, <c>camera-sim</c>) a published manifest omits
    /// because it publishes in publish mode, not run mode (spec 187 §2.1). Not
    /// the shape CI's end-to-end job or <c>aspire run</c> boots — neither passes a <c>Parameters:</c> argument.
    /// </summary>
    [Fact]
    public async Task Every_container_image_a_run_mode_stack_composes_is_pinned()
    {
        PulledImage[] images = await PulledImagesAsync(RunModeArguments);

        AssertAllPinned(images);
    }

    /// <summary>
    /// The shape CI's own integration lane boots. <c>camera-sim</c> is dev-only
    /// and gone; <c>fixture-video</c> stays (spec 076, #198).
    /// </summary>
    [Fact]
    public async Task Every_container_image_the_integration_fixture_composes_is_pinned()
    {
        PulledImage[] images = await PulledImagesAsync(FixtureArguments);

        images.Length.ShouldBeGreaterThanOrEqualTo(
            6,
            $"expected at least the six pulled containers the integration fixture composes today "
            + $"(postgres, rabbitmq, keycloak, mediamtx, fixture-video, minio) and found "
            + $"{images.Length}: {string.Join(", ", images.Select(image => image.ResourceName))}. Either "
            + "a container was removed (update this number and say why) or the scan no longer finds "
            + "what it should — the scan is broken, not the code.");

        AssertAllPinned(images);
    }

    /// <summary>
    /// The positive control this class rests on, and the justification for
    /// rejecting a manifest-publish-based guard (spec 187 §2.1, §2.6): a
    /// published manifest is built in publish mode, so it can never see either
    /// of these two resources. A guard whose population is empty passes having
    /// checked nothing — the same failure mode
    /// <c>ContainerImagePinTests</c> guards against for its own scan.
    /// </summary>
    [Fact]
    public async Task The_run_mode_population_includes_the_containers_a_published_manifest_omits()
    {
        PulledImage[] images = await PulledImagesAsync(RunModeArguments);

        string[] names = [.. images.Select(image => image.ResourceName)];

        names.ShouldContain("fixture-video");
        names.ShouldContain("camera-sim");

        images.Length.ShouldBeGreaterThanOrEqualTo(
            8,
            $"expected at least the eight pulled containers a run-mode stack composes today "
            + $"(mediamtx, fixture-video, camera-sim, postgres, pgadmin, rabbitmq, keycloak, minio) and found "
            + $"{images.Length}: {string.Join(", ", names)}. Either a container was removed (update "
            + "this number and say why) or the scan no longer finds what it should — the scan is "
            + "broken, not the code.");
    }

    /// <summary>
    /// <c>mosquitto</c> is declared with <c>AddDockerfile</c> and carries a
    /// <see cref="DockerfileBuildAnnotation"/>: nothing is pulled for its own
    /// reference — its tag is content-addressed by Aspire, and judging it
    /// against a registry tag would fail the build over a string no registry
    /// ever sees. Its Dockerfile's own base images
    /// (<c>src/AppHost/mosquitto/Dockerfile</c>) are a separate, currently
    /// unguarded surface — no guard in this repository reads a Dockerfile (spec
    /// 187 §9). The exclusion here is by <b>annotation</b>, never by resource
    /// name — a name list is a narrowing that grows silently, and an annotation
    /// check admits the next <c>AddDockerfile</c> resource automatically.
    /// </summary>
    [Fact]
    public async Task A_locally_built_image_is_not_judged_against_a_registry_tag()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);

        IResource mosquitto = builder.Resources.Single(resource => resource.Name == "mosquitto");

        mosquitto.Annotations.OfType<DockerfileBuildAnnotation>().ShouldNotBeEmpty(
            "expected mosquitto to be declared with AddDockerfile — if it now pulls a published "
            + "image instead, it belongs in the pinned population above, not in this exclusion.");

        PulledImage[] images = await PulledImagesAsync(RunModeArguments);

        images.Select(image => image.ResourceName).ShouldNotContain("mosquitto");
    }

    /// <summary>
    /// <c>keycloak</c>'s image coordinates come from <c>Aspire.Hosting.Keycloak</c>
    /// today, not from a literal in this repository — <c>ContainerImagePinTests</c>
    /// reads source and cannot see a package-supplied default at all (issue
    /// #2270). This fact is the characterisation this repository's ADR-0144 lane
    /// requires before <c>AppHost.cs</c> is touched to spell the coordinates out
    /// explicitly (issue #2296, spec 195): it is green on the unmodified file
    /// because the package supplies exactly these coordinates already, and it
    /// must pass <b>unmodified</b> once they are written — an edit here would be
    /// evidence the pin changed what runs.
    ///
    /// <para>
    /// <b>Deliberately does not name the tag.</b> Naming <c>26.6</c> (or whatever
    /// a future pin becomes) would make a legitimate Keycloak bump edit its own
    /// guard, which is how a guard gets deleted within a month
    /// (<c>FoundingDecisionRecordTests</c>' argument). Only the repository,
    /// registry and the shape of "present and not floating" are asserted.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_keycloak_image_resolves_to_the_upstream_repository_this_stack_expects()
    {
        PulledImage[] images = await PulledImagesAsync(RunModeArguments);
        PulledImage keycloak = images.Single(image => image.ResourceName == "keycloak");

        keycloak.Image.ShouldBe("keycloak/keycloak");
        keycloak.Registry.ShouldBe("quay.io");
        IsPinned(keycloak).ShouldBeTrue(
            $"expected keycloak's tag to be present and not floating and found '{keycloak.Reference}' — "
            + "a package bump could float the default tag with no diff in AppHost.cs (#2270).");
    }

    /// <summary>
    /// A reference is pinned when its digest is set, or when its tag is
    /// present and no hyphen-separated segment is <c>latest</c> — the same
    /// definition <c>ContainerImagePinTests.IsFloating</c> applies to the
    /// literals it can see, reproduced exactly (spec 187 §2.3). Widening it
    /// (to catch <c>rabbitmq:4-management-alpine</c>'s floating-within-major)
    /// is a supply-chain decision affecting five images and is deliberately
    /// out of scope here (spec 187 §9).
    /// </summary>
    private static void AssertAllPinned(PulledImage[] images)
    {
        images.Length.ShouldBeGreaterThan(
            0,
            "the composed model resolved no pulled container image at all — the scan is broken, "
            + "not the code. A guard that matches nothing passes, and a passing guard that checks "
            + "nothing is indistinguishable from one that holds.");

        PulledImage[] floating = [.. images.Where(image => !IsPinned(image))];

        floating.ShouldBeEmpty(Explain(floating));
    }

    private static bool IsPinned(PulledImage image) =>
        image.SHA256 is not null
        || (!string.IsNullOrEmpty(image.Tag) && !IsFloating(image.Tag));

    private static bool IsFloating(string tag) =>
        tag.Split('-').Any(segment => segment.Equals("latest", StringComparison.OrdinalIgnoreCase));

    private static string Explain(PulledImage[] floating) =>
        $"{floating.Length} container image(s) the composed application model actually pulls "
        + "resolve to a floating or absent tag:"
        + Environment.NewLine
        + string.Join(Environment.NewLine, floating.Select(image => $"  {image.ResourceName} → {image.Reference}"))
        + Environment.NewLine
        + "A floating tag changes what runs on the next pull, with no diff and no PR (#2103). "
        + "Architecture.Tests.ContainerImagePinTests reads AppHost.cs as text and cannot see this: "
        + "it has no notion of call order, so a WithImage call after WithImageTag can overwrite a "
        + "pin it still matches and still passes on (#2270), and it cannot see a package-supplied "
        + "default such as keycloak's at all.";

    private static async Task<PulledImage[]> PulledImagesAsync(string[] arguments)
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(arguments);

        return
        [
            .. builder.Resources
                .Where(resource => !resource.Annotations.OfType<DockerfileBuildAnnotation>().Any())
                .SelectMany(
                    resource => resource.Annotations.OfType<ContainerImageAnnotation>(),
                    (resource, image) => new PulledImage(resource.Name, image.Registry, image.Image, image.Tag, image.SHA256)),
        ];
    }

    /// <summary>
    /// One <see cref="ContainerImageAnnotation"/> read off a resource the model
    /// actually composed, kept minimal so a failure message needs no second
    /// lookup back into the model.
    /// </summary>
    private sealed record PulledImage(string ResourceName, string? Registry, string Image, string? Tag, string? SHA256)
    {
        /// <summary>
        /// Rendered the way a reader would write it, so a failure message can
        /// be pasted into <c>docker pull</c> — prefixed with the registry when
        /// one is set, because <c>minio</c> resolves through <c>quay.io</c> and
        /// a message that omits it sends the reader to the wrong registry.
        /// </summary>
        public string Reference
        {
            get
            {
                string reference = SHA256 is null ? $"{Image}:{Tag}" : $"{Image}@sha256:{SHA256}";
                return Registry is null ? reference : $"{Registry}/{reference}";
            }
        }
    }
}
