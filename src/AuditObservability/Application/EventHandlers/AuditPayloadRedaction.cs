using System.Collections.Frozen;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.EventHandlers;

/// <summary>
/// Reduces the camera addresses an integration event carries to their origin before the audit copy is
/// serialised (spec 377). The contract and every other consumer keep the full URL; only the stored,
/// returned and archived audit payload is redacted.
/// </summary>
/// <remarks>
/// The register and the code that applies it are one table (the fields, how each is redacted and any member
/// appended to the stored JSON), so a field cannot be listed without being redacted. Each delivery is
/// checked against the register by reflection, and a registered field that cannot be redacted throws: a
/// missing audit row is visible, an unredacted one is the defect. Redaction is idempotent, so re-applying
/// it to an already redacted payload is safe.
/// </remarks>
public static class AuditPayloadRedaction
{
    private static readonly FrozenDictionary<Type, Registration> Registrations =
        new Dictionary<Type, Registration>
        {
            [typeof(CameraRegisteredV1)] = new(
                [nameof(CameraRegisteredV1.Url)],
                message =>
                {
                    var registered = (CameraRegisteredV1)message;
                    return registered with { Url = Origin(registered.Url) };
                }),
            [typeof(CameraAddressChangedV1)] = new(
                [nameof(CameraAddressChangedV1.PreviousUrl), nameof(CameraAddressChangedV1.Url)],
                message =>
                {
                    var change = (CameraAddressChangedV1)message;
                    return change with { PreviousUrl = Origin(change.PreviousUrl), Url = Origin(change.Url) };
                },
                message =>
                {
                    var change = (CameraAddressChangedV1)message;
                    return DetailChangedMember(DetailChanged(change.PreviousUrl, change.Url));
                }),
        }.ToFrozenDictionary();

    public static IReadOnlyDictionary<Type, IReadOnlyList<string>> UrlFields { get; } =
        Registrations.ToFrozenDictionary(entry => entry.Key, entry => entry.Value.Fields);

    public static string Serialize(IIntegrationEvent message)
    {
        Ensure.That(message).IsNotNull();

        var type = message.GetType();

        if (!Registrations.TryGetValue(type, out var registration))
        {
            return JsonSerializer.Serialize(message, type);
        }

        var redacted = registration.Redact(message);
        VerifyRedacted(type, message, redacted, registration.Fields);

        var json = JsonSerializer.Serialize(redacted, type);

        return registration.Suffix is { } suffix
            ? AppendMember(type, json, suffix(message))
            : json;
    }

    internal static string AppendMember(Type type, string json, string member)
    {
        if (!json.EndsWith('}'))
        {
            throw new InvalidOperationException($"Audit redaction could not append to the serialised {type.Name}.");
        }

        return string.Concat(
            json.AsSpan(0, json.Length - 1),
            ",",
            member,
            "}");
    }

    // The contract declares the property non-nullable, but a wire message can carry null (FR-005); the
    // redacted copy must carry it through unchanged rather than fail the audit write.
    private static string Origin(string? url) => url is null ? null! : UrlRedaction.ToOrigin(url);

    private static void VerifyRedacted(
        Type type,
        IIntegrationEvent original,
        IIntegrationEvent redacted,
        IReadOnlyList<string> fields)
    {
        foreach (var field in fields)
        {
            var property = type.GetProperty(field);

            if (property is null
                || property.PropertyType != typeof(string)
                || !string.Equals(
                    (string?)property.GetValue(redacted),
                    Origin((string?)property.GetValue(original)),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Audit redaction could not redact field {field} of {type.Name}.");
            }
        }
    }

    private static string DetailChangedMember(bool detailChanged) =>
        detailChanged ? "\"DetailChanged\":true" : "\"DetailChanged\":false";

    private static bool DetailChanged(string? previousUrl, string? url) =>
        previousUrl is not null
        && url is not null
        && string.Equals(UrlRedaction.ToOrigin(previousUrl), UrlRedaction.ToOrigin(url), StringComparison.Ordinal)
        && !string.Equals(previousUrl, url, StringComparison.Ordinal);

    private sealed record Registration(
        IReadOnlyList<string> Fields,
        Func<IIntegrationEvent, IIntegrationEvent> Redact,
        Func<IIntegrationEvent, string>? Suffix = null);
}
