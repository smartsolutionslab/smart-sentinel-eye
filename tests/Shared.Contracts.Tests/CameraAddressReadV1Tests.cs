using System.Globalization;
using System.Reflection;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.Shared.Contracts.Tests;

/// <summary>
/// Spec 438 (#3016, ADR-0179): the event announces that a camera's full address was read, and must never
/// be able to carry it. The shape is the guard: the only text members are the fab, and no member is named
/// like an address.
///
/// <para>
/// <see cref="ProbeV1"/> is the counterfactual: a field renamed to <c>StreamEndpoint</c> passes a names-only
/// check, and only the type check reports it.
/// </para>
/// </summary>
public class CameraAddressReadV1Tests
{
    private static readonly string[] AddressLikeNameParts = ["Url", "Uri", "Address"];

    private sealed record ProbeV1(Guid Camera, string Fab, string StreamEndpoint, EventMetadata Metadata);

    [Fact]
    public void The_only_text_members_are_the_fab_and_the_metadata_fab()
    {
        var text = TextMembersOf(typeof(CameraAddressReadV1));

        text.ShouldBe(["Fab", "Metadata.Fab"], ignoreOrder: true);
    }

    [Fact]
    public void No_member_is_named_like_an_address()
    {
        AddressLikeNamesIn(typeof(CameraAddressReadV1)).ShouldBeEmpty();
    }

    [Fact]
    public void The_text_member_check_reports_a_renamed_address_field()
    {
        TextMembersOf(typeof(ProbeV1)).ShouldContain("StreamEndpoint");
    }

    [Fact]
    public void The_name_check_alone_would_miss_a_renamed_address_field()
    {
        AddressLikeNamesIn(typeof(ProbeV1)).ShouldBeEmpty();
    }

    [Fact]
    public void Exposes_the_camera_reader_moment_and_metadata_via_the_positional_constructor()
    {
        var camera = Guid.CreateVersion7();
        var reader = Guid.CreateVersion7();
        var moment = DateTimeOffset.Parse("2026-10-22T09:00:00Z", CultureInfo.InvariantCulture);
        EventMetadata metadata = new(Guid.CreateVersion7(), moment, "munich", reader);

        CameraAddressReadV1 evt = new(camera, "munich", reader, moment, metadata);

        evt.Camera.ShouldBe(camera);
        evt.Fab.ShouldBe("munich");
        evt.ReadBy.ShouldBe(reader);
        evt.ReadAt.ShouldBe(moment);
        evt.Metadata.ShouldBe(metadata);
    }

    private static List<string> TextMembersOf(Type type, string prefix = "")
    {
        List<string> found = [];

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var path = prefix + property.Name;
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (propertyType == typeof(string) || propertyType == typeof(Uri))
            {
                found.Add(path);
            }
            else if (IsNested(propertyType))
            {
                found.AddRange(TextMembersOf(propertyType, path + "."));
            }
        }

        return found;
    }

    private static List<string> AddressLikeNamesIn(Type type, string prefix = "")
    {
        List<string> found = [];

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var path = prefix + property.Name;
            if (AddressLikeNameParts.Any(part => property.Name.Contains(part, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(path);
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (IsNested(propertyType))
            {
                found.AddRange(AddressLikeNamesIn(propertyType, path + "."));
            }
        }

        return found;
    }

    private static bool IsNested(Type type) =>
        type.IsClass
        && type != typeof(string)
        && type != typeof(Uri)
        && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true;
}
