using System.Reflection;
using SmartSentinelEye.ScenarioSimulator.Configuration;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Characterisation of <c>BindRuntime</c> in the simulator's <c>Program.cs</c> (spec 342,
/// behaviour-preserving). <c>BindRuntime</c> is a static local function of the top-level program, so
/// it is reached by reflection on the compiler-generated name (<c>g__BindRuntime|</c>); the lookup
/// fails loudly, naming the candidates, if the compiler's scheme or the function's name changes.
/// </summary>
public sealed class SimulatorRuntimeBindingTests
{
    private const string Prefix = "ScenarioSimulator:Runtime:";

    private static readonly Dictionary<string, string> AspireEndpoints = new()
    {
        ["services:camera-catalog:http:0"] = "http://aspire-catalog",
        ["services:camera-sim:api:0"] = "http://aspire-sim",
        ["services:overlay-designer:http:0"] = "http://aspire-overlay",
        ["services:automation:http:0"] = "http://aspire-automation",
        ["services:layout-composition:http:0"] = "http://aspire-layout",
        ["services:system-variables:http:0"] = "http://aspire-variables",
        ["services:mosquitto:mqtt:0"] = "aspire-mqtt:1883",
        ["services:keycloak:http:0"] = "http://aspire-keycloak",
    };

    private static readonly Dictionary<string, string> FallbackEndpoints = new()
    {
        [Prefix + "CameraCatalogUrl"] = "http://fallback-catalog",
        [Prefix + "CameraSimApiUrl"] = "http://fallback-sim",
        [Prefix + "OverlayDesignerUrl"] = "http://fallback-overlay",
        [Prefix + "AutomationUrl"] = "http://fallback-automation",
        [Prefix + "LayoutCompositionUrl"] = "http://fallback-layout",
        [Prefix + "SystemVariablesUrl"] = "http://fallback-variables",
        [Prefix + "MqttHost"] = "fallback-mqtt:1883",
    };

    private static MethodInfo BindRuntimeMethod()
    {
        var program = typeof(SimulatorOptions).Assembly.EntryPoint?.DeclaringType
            ?? throw new InvalidOperationException("The simulator assembly has no entry point type.");

        var staticMethods = program.GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
        var candidates = staticMethods
            .Where(method => method.Name.Contains("g__BindRuntime|", StringComparison.Ordinal))
            .ToList();

        candidates.Count.ShouldBe(
            1,
            $"Expected exactly one BindRuntime local function on {program.FullName}; "
            + $"static methods found: [{string.Join(", ", staticMethods.Select(method => method.Name))}].");

        return candidates[0];
    }

    private static SimulatorOptions Bind(Dictionary<string, string> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();
        SimulatorOptions options = new();

        BindRuntimeMethod().Invoke(
            null,
            BindingFlags.DoNotWrapExceptions,
            null,
            [configuration, options],
            null);

        return options;
    }

    private static Dictionary<string, string> Merge(params Dictionary<string, string>[] parts) =>
        parts.SelectMany(part => part).ToDictionary(pair => pair.Key, pair => pair.Value);

    private static Dictionary<string, string> Complete() =>
        Merge(AspireEndpoints, FallbackEndpoints, new() { [Prefix + "ClientSecret"] = "s3cret" });

    private static string Missing(Dictionary<string, string> settings, params string[] keys)
    {
        foreach (var key in keys)
        {
            settings.Remove(key);
        }

        var exception = Should.Throw<InvalidOperationException>(() => Bind(settings));
        return exception.Message;
    }

    [Fact]
    public void The_bind_runtime_local_function_is_found_exactly_once()
    {
        BindRuntimeMethod().GetParameters().Length.ShouldBe(2);
    }

    [Fact]
    public void Aspire_injected_keys_win_over_the_fallback_keys()
    {
        var options = Bind(Complete());

        options.CameraCatalogUrl.ShouldBe("http://aspire-catalog");
        options.CameraSimApiUrl.ShouldBe("http://aspire-sim");
        options.OverlayDesignerUrl.ShouldBe("http://aspire-overlay");
        options.AutomationUrl.ShouldBe("http://aspire-automation");
        options.LayoutCompositionUrl.ShouldBe("http://aspire-layout");
        options.SystemVariablesUrl.ShouldBe("http://aspire-variables");
        options.MqttHost.ShouldBe("aspire-mqtt:1883");
        options.KeycloakUrl.ShouldBe("http://aspire-keycloak");
        options.ClientSecret.ShouldBe("s3cret");
    }

    [Fact]
    public void Realm_client_id_and_rtsp_host_keep_their_defaults_when_unset()
    {
        var options = Bind(Complete());

        options.Realm.ShouldBe("smart-sentinel-eye");
        options.ClientId.ShouldBe("scenario-simulator");
        options.RtspHost.ShouldBe("camera-sim:8554");
    }

    [Fact]
    public void Fallback_keys_are_used_when_the_aspire_keys_are_absent()
    {
        var settings = Merge(
            FallbackEndpoints,
            new()
            {
                ["services:keycloak:https:0"] = "https://aspire-keycloak",
                [Prefix + "ClientSecret"] = "s3cret",
            });

        var options = Bind(settings);

        options.CameraCatalogUrl.ShouldBe("http://fallback-catalog");
        options.CameraSimApiUrl.ShouldBe("http://fallback-sim");
        options.OverlayDesignerUrl.ShouldBe("http://fallback-overlay");
        options.AutomationUrl.ShouldBe("http://fallback-automation");
        options.LayoutCompositionUrl.ShouldBe("http://fallback-layout");
        options.SystemVariablesUrl.ShouldBe("http://fallback-variables");
        options.MqttHost.ShouldBe("fallback-mqtt:1883");
        options.KeycloakUrl.ShouldBe("https://aspire-keycloak");
    }

    [Theory]
    [InlineData(true, true, true, "conn")]
    [InlineData(false, true, true, "http://aspire-keycloak")]
    [InlineData(false, false, true, "https://aspire-keycloak")]
    public void Keycloak_url_prefers_the_connection_string_then_http_then_https(
        bool connectionString,
        bool http,
        bool https,
        string expected)
    {
        var settings = Merge(AspireEndpoints, new() { [Prefix + "ClientSecret"] = "s3cret" });
        settings.Remove("services:keycloak:http:0");
        if (connectionString)
        {
            settings["ConnectionStrings:keycloak"] = "conn";
        }

        if (http)
        {
            settings["services:keycloak:http:0"] = "http://aspire-keycloak";
        }

        if (https)
        {
            settings["services:keycloak:https:0"] = "https://aspire-keycloak";
        }

        Bind(settings).KeycloakUrl.ShouldBe(expected);
    }

    [Fact]
    public void Realm_client_id_and_rtsp_host_are_read_from_the_runtime_section_when_set()
    {
        var settings = Complete();
        settings[Prefix + "Realm"] = "other-realm";
        settings[Prefix + "ClientId"] = "other-client";
        settings[Prefix + "RtspHost"] = "other-host:9554";

        var options = Bind(settings);

        options.Realm.ShouldBe("other-realm");
        options.ClientId.ShouldBe("other-client");
        options.RtspHost.ShouldBe("other-host:9554");
    }

    [Fact]
    public void An_empty_aspire_value_is_a_value_and_does_not_fall_through_to_the_fallback()
    {
        var settings = Complete();
        settings["services:camera-catalog:http:0"] = string.Empty;

        Bind(settings).CameraCatalogUrl.ShouldBe(string.Empty);
    }

    [Fact]
    public void An_empty_configuration_fails_on_the_camera_catalog_first()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Bind([]));

        exception.Message.ShouldBe("camera-catalog URL not configured (services:camera-catalog:http:0).");
    }

    [Theory]
    [InlineData("services:camera-catalog:http:0", "CameraCatalogUrl", "camera-catalog URL not configured (services:camera-catalog:http:0).")]
    [InlineData("services:camera-sim:api:0", "CameraSimApiUrl", "camera-sim API URL not configured (services:camera-sim:api:0).")]
    [InlineData("services:overlay-designer:http:0", "OverlayDesignerUrl", "overlay-designer URL not configured (services:overlay-designer:http:0).")]
    [InlineData("services:automation:http:0", "AutomationUrl", "automation URL not configured (services:automation:http:0).")]
    [InlineData("services:layout-composition:http:0", "LayoutCompositionUrl", "layout-composition URL not configured (services:layout-composition:http:0).")]
    [InlineData("services:system-variables:http:0", "SystemVariablesUrl", "system-variables URL not configured (services:system-variables:http:0).")]
    [InlineData("services:mosquitto:mqtt:0", "MqttHost", "mosquitto MQTT host not configured (services:mosquitto:mqtt:0).")]
    public void A_missing_endpoint_setting_names_itself_in_the_failure(
        string aspireKey,
        string fallbackName,
        string expectedMessage)
    {
        var message = Missing(Complete(), aspireKey, Prefix + fallbackName);

        message.ShouldBe(expectedMessage);
    }

    [Fact]
    public void A_missing_keycloak_url_names_itself_in_the_failure()
    {
        var message = Missing(Complete(), "services:keycloak:http:0");

        message.ShouldBe("Keycloak URL not configured for the scenario simulator.");
    }

    [Fact]
    public void A_missing_client_secret_names_itself_in_the_failure()
    {
        var message = Missing(Complete(), Prefix + "ClientSecret");

        message.ShouldBe("scenario-simulator client secret not configured.");
    }

    [Fact]
    public void With_several_settings_missing_the_first_in_binding_order_is_reported()
    {
        var message = Missing(
            Complete(),
            "services:system-variables:http:0",
            Prefix + "SystemVariablesUrl",
            "services:mosquitto:mqtt:0",
            Prefix + "MqttHost",
            Prefix + "ClientSecret");

        message.ShouldBe("system-variables URL not configured (services:system-variables:http:0).");
    }
}
