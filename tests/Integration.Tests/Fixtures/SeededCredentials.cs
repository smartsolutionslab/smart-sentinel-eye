namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Spec 325 (issue #2510) — one <c>public const string</c> per seeded human
/// account in <c>src/AppHost/Realms/smart-sentinel-eye-realm.json</c>, so a
/// future rotation is a one-file change rather than 54 literal swaps
/// (plan.md §4). Every value here must stay byte-for-byte equal to the
/// realm import's corresponding <c>credentials[0].value</c> — the realm file
/// remains the source of truth; this class mirrors it for test call sites.
///
/// <para>
/// D1 (plan.md §2's gate question, taken): twelve distinct values, none
/// shared across accounts and none derivable from a username, fab or role —
/// so, unlike the retired single-value-shared-by-six-accounts shape it
/// replaces, no two members of this class are equal and a file that used to
/// share one <c>OperatorPassword</c> constant between two usernames must now
/// name both.
/// </para>
/// </summary>
public static class SeededCredentials
{
    public const string WallMunich = "Copper-Lantern-Drift-47";
    public const string WallDresden = "Maple-Orbit-Canvas-82";
    public const string WallBerlin = "Quartz-Harbor-Ember-19";
    public const string WallHamburg = "Velvet-Summit-Prism-63";
    public const string Admin = "Granite-Willow-Beacon-58";
    public const string Operator = "Cobalt-Meadow-Ripple-24";
    public const string AdminMunich = "Saffron-Glacier-Tundra-71";
    public const string Op3Munich = "Hazel-Compass-Thistle-36";
    public const string OpDresden = "Indigo-Falcon-Pebble-90";
    public const string OpMulti = "Juniper-Anchor-Mosaic-15";
    public const string OpBerlin = "Marble-Cinder-Lagoon-43";
    public const string OpHamburg = "Amber-Trellis-Nimbus-68";
}
