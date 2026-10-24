using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 389 / issue #2999, adversarial (T050). The two camera URL value objects are declared separately in two
/// contexts and are meant to be the same rule; <see cref="UrlRedaction.ToOrigin"/> is meant to refuse to render
/// exactly what they refuse to store. Over one hand-tagged corpus this asserts, row by row, that the two
/// <c>From</c>s give the same answer, that the two <c>FromStored</c>s give the same answer, that a raw-'@' refusal
/// happens exactly when the shared predicate fires and the older rules pass, that <c>ToOrigin</c> renders the
/// placeholder for every such row, and that each EF converter's read lambda answers exactly what
/// <c>FromStored</c> answers: it accepts what the raw-'@' rule alone refuses and refuses everything else
/// <c>From</c> refused before that rule, with the same message.
/// </summary>
public class CameraUrlRefusalAgreementTests
{
    private const string AtSignRefusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    public static TheoryData<string> Corpus =>
    [
        "rtsp://admin:12345#@cam/s",
        "rtsp://admin:4711?@cam/s",
        "rtsp://admin:8080/x@cam/s",
        "RTSP://ADMIN:12345#@CAM",
        "rtsp://3232235777:12345#@cam",
        "RtSp://admin:4242#@cam",
        "rtsp://cam/s?u=admin:4242@x",
        "rtsp://cam/s#admin:4242@x",
        "rtsp://cam/s\\admin:4242@x",
        "rtsp://cam/admin:4242@x",
        "rtsp://admin:4242#@x@cam/s",
        "rtsp://cam/a@b@c",
        "rtsp://[fd00::5]:8554/4242@y",
        "rtsp://admin:4242#@[fd00::5]:8554/x",
        "rtsp://admin:4242#@cam/s ",
        "rtsp://admin:4242#\t@cam",
        "rtsp://admin:4242#\0@cam/s",
        "rtsp://admin:4242#@cam./s",
        "rtsp:///admin:4242#@cam",
        "rtsp://admin:4242#@c\u00E4m",
        "rtsp://cam#@",
        "rtsp://cam/" + new string('a', 2048 - 12) + "@",
        "rtsp://cam/" + new string('a', 2048 - 11) + "@",
        "rtsp://admin:4242@cam/s",
        "rtsp://admin:4242%23@cam/s",
        "rtsp://admin:4242%40cam/s",
        "rtsp://a:4242@c@cam/s",
        "rtsp://[fd00::1]:4711@cam/s",
        "rtsp://admin:4242\\@cam/s",
        "rtsp://\\admin:4242#@cam",
        "rtsp://admin:4242\u200E#@cam",
        "rtsp://admin:4242\uFF20cam/s",
        " rtsp://admin:4242#@cam",
        "rtsp:\\\\admin:4242#@cam",
        "http://admin:4242#@cam",
        "rtsp://cam/%40x",
        "rtsp://cam/s?mail=a%40b",
        "rtsp://[fe80::1%25eth0]/s",
        "rtsp://[::1]:554/s",
        "rtsp://cam./s",
        "RtSp://CAM/s",
    ];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_two_write_factories_give_the_same_answer(string address)
    {
        Outcome(() => RtspUrl.From(address).Value).ShouldBe(Outcome(() => StreamSourceUrl.From(address).Value));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_two_read_factories_give_the_same_answer(string address)
    {
        var storedCamera = Outcome(() => RtspUrl.FromStored(address).Value);

        if (Outcome(() => RtspUrl.From(address).Value) == "refused: " + UserInfoRefusal)
        {
            storedCamera.ShouldBe("accepted: " + address);
        }
        else
        {
            storedCamera.ShouldBe(Outcome(() => StreamSourceUrl.FromStored(address).Value));
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_raw_at_sign_refusal_happens_exactly_when_the_predicate_fires_and_the_older_rules_pass(string address)
    {
        var refusedOnTheAtSignRule = Outcome(() => RtspUrl.From(address).Value) == "refused: " + AtSignRefusal;
        var userInfoRefused = Outcome(() => RtspUrl.From(address).Value) == "refused: " + UserInfoRefusal;
        var olderRulesPass = Outcome(() => RtspUrl.FromStored(address).Value) == "accepted: " + address && !userInfoRefused;

        refusedOnTheAtSignRule.ShouldBe(olderRulesPass && UrlAuthority.AtSignFollowsTheAuthority(address));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Whatever_From_refuses_on_the_at_sign_rule_ToOrigin_will_not_render_and_nothing_accepted_is_flagged(string address)
    {
        var outcome = Outcome(() => RtspUrl.From(address).Value);

        if (outcome == "refused: " + AtSignRefusal)
        {
            UrlRedaction.ToOrigin(address).ShouldBe(UrlRedaction.Unparseable);
        }
        else if (outcome.StartsWith("accepted: ", StringComparison.Ordinal))
        {
            UrlAuthority.AtSignFollowsTheAuthority(address).ShouldBeFalse(
                "an accepted address the shared predicate flags would be stored by From yet withheld by ToOrigin");
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_camera_converter_reads_exactly_what_FromStored_reads(string address)
    {
        var converter = CameraUrlProperty().GetValueConverter().ShouldNotBeNull();

        Outcome(() => ((RtspUrl)converter.ConvertFromProvider(address)!).Value)
            .ShouldBe(Outcome(() => RtspUrl.FromStored(address).Value));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_stream_converter_reads_exactly_what_FromStored_reads(string address)
    {
        var converter = StreamSourceUrlProperty().GetValueConverter().ShouldNotBeNull();

        Outcome(() => ((StreamSourceUrl)converter.ConvertFromProvider(address)!).Value)
            .ShouldBe(Outcome(() => StreamSourceUrl.FromStored(address).Value));
    }

    [Fact]
    public void The_corpus_exercises_all_three_outcomes()
    {
        var outcomes = Corpus
            .Cast<object[]>()
            .Select(row => (string)row[0])
            .Select(address => Outcome(() => RtspUrl.From(address).Value))
            .ToList();

        outcomes.Count(outcome => outcome == "refused: " + AtSignRefusal).ShouldBeGreaterThan(10);
        outcomes.Count(outcome => outcome.StartsWith("accepted: ", StringComparison.Ordinal)).ShouldBeGreaterThan(3);
        outcomes.Count(outcome => outcome.StartsWith("refused: ", StringComparison.Ordinal) && outcome != "refused: " + AtSignRefusal)
            .ShouldBeGreaterThan(5);
    }

    private static string Outcome(Func<string> read)
    {
        try
        {
            return "accepted: " + read();
        }
        catch (ArgumentException refused)
        {
            return "refused: " + refused.Message;
        }
    }

    private static IProperty CameraUrlProperty()
    {
        var options = new DbContextOptionsBuilder<CameraCatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using CameraCatalogDbContext context = new(options);

        return DesignTimeModel(context).FindEntityType(typeof(Camera))!.FindProperty(nameof(Camera.Url))!;
    }

    private static IProperty StreamSourceUrlProperty()
    {
        var options = new DbContextOptionsBuilder<StreamDistributionDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using StreamDistributionDbContext context = new(options);

        return DesignTimeModel(context)
            .FindEntityType(typeof(StreamDistribution.Domain.Stream.Stream))!
            .FindProperty(nameof(StreamDistribution.Domain.Stream.Stream.SourceUrl))!;
    }

    private static IModel DesignTimeModel(DbContext context) =>
        ((IInfrastructure<IServiceProvider>)context).Instance.GetService(typeof(IDesignTimeModel)) is IDesignTimeModel designTimeModel
            ? designTimeModel.Model
            : throw new InvalidOperationException("The design-time model is unavailable.");
}
