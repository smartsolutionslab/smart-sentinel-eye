using System.Text.Json;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// The shape of the wall-display client <b>as declared</b> (spec 052).
///
/// <para>
/// <b>Read this limitation before trusting anything here.</b> These tests read
/// the realm file. A file says what is <i>declared</i>; it cannot say what an
/// account in a running realm actually <i>holds</i>, because the provider grants
/// every account it creates a default privilege the file never mentions. Spec
/// 050's guard read this same file, stayed green for an entire feature, and the
/// claim it was taken to support — that only wall displays could mint a
/// never-expiring credential — was false the whole time.
/// </para>
///
/// <para>
/// So these cover exactly two things, both genuinely properties of the
/// declaration: that the wall client does not carry write authority, and that
/// the long-lived-credential scope is offered <i>optionally</i> rather than
/// forced on everyone. <b>Who holds the privilege is asked of the running
/// provider</b> in <c>KioskInheritedPrivilegeIntegrationTests</c>, and nowhere
/// else.
/// </para>
/// </summary>
public class WallClientDeclarationTests
{
    private const string WallClient = "kiosk-wall";
    private const string KioskClient = "kiosk-web";
    private const string LongLivedCredentialScope = "offline_access";
    private const string WriteScope = "sse.events.write";

    /// <summary>
    /// **The narrowing, and it is the whole reason a second client exists.**
    ///
    /// <para>
    /// Scopes belong to clients, so this is the only place a wall display's
    /// authority can be reduced. A never-expiring grant carrying write authority
    /// could inject events into its fab indefinitely — which is what spec 050
    /// would have shipped while recording that the account could change nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void A_wall_display_carries_no_write_authority()
    {
        JsonElement wall = Client(WallClient);

        Scopes(wall, "defaultClientScopes").ShouldNotContain(WriteScope);
        Scopes(wall, "optionalClientScopes").ShouldNotContain(WriteScope);
    }

    /// <summary>
    /// A wall display must still be able to show a wall, or the narrowing has
    /// gone too far and this reads as a passing test for a screen that renders
    /// nothing.
    /// </summary>
    [Fact]
    public void A_wall_display_can_still_read_what_a_wall_needs()
    {
        string[] declared = Scopes(Client(WallClient), "defaultClientScopes");

        foreach (string needed in new[]
                 {
                     "sse-identity", "sse-groups", "sse.cameras.read", "sse.streams.read",
                     "sse.layouts.read", "sse.overlays.read", "sse.variables.read",
                 })
        {
            declared.ShouldContain(needed);
        }
    }

    /// <summary>
    /// **Optional, never default.**
    ///
    /// <para>
    /// A default scope is mandatory: the provider refuses the entire sign-in for
    /// any account without the matching privilege. Making this one a default is
    /// exactly what made spec 050 unshippable — every operator, and six kiosk
    /// end-to-end specs, locked out of the app.
    /// </para>
    /// </summary>
    [Fact]
    public void The_long_lived_credential_scope_is_offered_and_never_forced()
    {
        JsonElement wall = Client(WallClient);

        Scopes(wall, "optionalClientScopes").ShouldContain(LongLivedCredentialScope);
        Scopes(wall, "defaultClientScopes").ShouldNotContain(
            LongLivedCredentialScope,
            "as a default it is mandatory, and every account without the privilege is refused sign-in entirely");
    }

    /// <summary>
    /// The ordinary kiosk client must not offer it at all — in either list.
    /// Requesting it there refuses every account that lacks the privilege, which
    /// is every operator.
    /// </summary>
    [Fact]
    public void The_ordinary_kiosk_client_does_not_offer_a_long_lived_credential()
    {
        JsonElement kiosk = Client(KioskClient);

        Scopes(kiosk, "defaultClientScopes").ShouldNotContain(LongLivedCredentialScope);
        Scopes(kiosk, "optionalClientScopes").ShouldNotContain(LongLivedCredentialScope);
    }

    /// <summary>
    /// A wall display signs in through the same browser flow as any other
    /// screen, so it needs the same redirect targets. Getting this wrong makes
    /// wall mode fail at the redirect rather than anywhere informative.
    /// </summary>
    [Fact]
    public void A_wall_display_signs_in_from_the_same_place_as_any_other_screen()
    {
        string[] wall = Strings(Client(WallClient), "redirectUris");
        string[] kiosk = Strings(Client(KioskClient), "redirectUris");

        wall.ShouldBe(kiosk, ignoreOrder: true);
    }

    /// <summary>
    /// **No client description may exceed 255 characters.**
    ///
    /// <para>
    /// Not a style rule. Keycloak stores it in a <c>VARCHAR(255)</c>, and a
    /// longer one fails the <b>entire realm import</b> — the provider exits,
    /// nothing starts, and the error names a database column rather than the
    /// file. This is written down because it has now cost time twice: it is in
    /// the project's notes, and it still got past a description of 733
    /// characters written to explain a design.
    /// </para>
    ///
    /// <para>
    /// Reasoning belongs in an ADR, which has no column width.
    /// </para>
    /// </summary>
    [Fact]
    public void No_client_description_exceeds_what_the_provider_can_store()
    {
        const int columnWidth = 255;

        foreach (JsonElement client in Realm().GetProperty("clients").EnumerateArray())
        {
            if (!client.TryGetProperty("description", out JsonElement description))
            {
                continue;
            }

            string text = description.GetString() ?? string.Empty;
            text.Length.ShouldBeLessThanOrEqualTo(
                columnWidth,
                $"'{client.GetProperty("clientId").GetString()}' has a {text.Length}-character description; "
                + "anything over 255 fails the whole realm import and nothing starts");
        }
    }

    private static string[] Scopes(JsonElement client, string property) => Strings(client, property);

    private static string[] Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            ? value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];

    private static JsonElement Client(string clientId) =>
        Realm().GetProperty("clients")
            .EnumerateArray()
            .First(client => client.GetProperty("clientId").GetString() == clientId);

    private static JsonElement Realm()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException($"could not locate the repository root above {AppContext.BaseDirectory}");

        string path = Path.Combine(root.FullName, "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");
        File.Exists(path).ShouldBeTrue($"the realm should be at {path}");

        // The file carries a byte-order mark, which the JSON reader rejects.
        return JsonDocument.Parse(File.ReadAllText(path).TrimStart('﻿')).RootElement;
    }
}
