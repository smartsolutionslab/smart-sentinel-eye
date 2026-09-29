using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Builds the <see cref="RuleSeed"/> for a declared <see cref="ReactionDefinition"/>
/// (spec 289 plan.md §3.4, §5.4), or refuses it — it never throws. Unlike
/// <see cref="HighlightRuleSeed"/> the trigger source is taken verbatim from
/// <c>reaction.When.Source</c>: a reaction states its own source, nothing is
/// derived from a matching sensor.
///
/// <para>
/// <c>HighlightOverlay</c> and <c>SetVariableValue</c> are the recognised
/// action <c>Type</c>s; any other value is refused the same way an unknown
/// one always would be, so adding a new action kind here is additive rather
/// than a behaviour change to the refusal path. A <c>SetVariableValue</c>
/// reaction is valid only when <c>reaction.Then.Variable</c> names an entry
/// in <c>asset.Variables</c> — declaration, not whether the seed itself
/// succeeded; that split happens in <see cref="ScenarioSeeder"/>.
/// </para>
/// </summary>
internal static class ReactionRuleSeed
{
    // Duplicated deliberately from Automation.Domain.Rule.RuleName's grammar:
    // a cross-context project reference is banned (NetArchTest), so this is
    // the simulator's own copy of the same rule, not a shortcut around it.
    private const int MinimumNameLength = 2;
    private const int MaximumNameLength = 63;

    internal static ReactionSeedResult From(string scenario, AssetDefinition asset, ReactionDefinition reaction, Guid? overlay)
    {
        Ensure.That(scenario).IsNotNull();
        Ensure.That(asset).IsNotNull();
        Ensure.That(reaction).IsNotNull();

        string name = $"{scenario}-{asset.Key}-{reaction.Name}";
        if (!IsValidRuleName(name))
        {
            return new ReactionSeedResult.Refused(
                $"'{name}' is not a valid rule name (2-63 chars, lowercase letter then lowercase/digit/'-').");
        }

        // Validated locally rather than left to AutomationRulesClient.EnsureRuleAsync: that call sits
        // inside ScenarioSeeder's per-asset try/catch, which is the I/O backstop, not the place a bad
        // reaction spec should be caught — a throw there costs the asset everything seeded after it,
        // camera registration included, not just the one reaction.
        if (string.IsNullOrWhiteSpace(reaction.When.Source))
        {
            return new ReactionSeedResult.Refused("trigger Source must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(reaction.When.Kind))
        {
            return new ReactionSeedResult.Refused("trigger Kind must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(reaction.When.Predicate))
        {
            return new ReactionSeedResult.Refused("trigger Predicate must not be blank.");
        }

        string predicate = $"$.device == '{asset.Camera.Path}' && ({reaction.When.Predicate})";

        switch (reaction.Then.Type)
        {
            case "HighlightOverlay":
                return FromHighlightOverlay(name, reaction, predicate, overlay);
            case "SetVariableValue":
                return FromSetVariableValue(name, asset, reaction, predicate);
            default:
                return new ReactionSeedResult.Refused(
                    $"action type '{reaction.Then.Type}' is not supported yet.");
        }
    }

    private static ReactionSeedResult FromHighlightOverlay(
        string name, ReactionDefinition reaction, string predicate, Guid? overlay)
    {
        if (overlay is null)
        {
            return new ReactionSeedResult.Refused("HighlightOverlay reaction on an asset with no seeded overlay.");
        }

        if (reaction.Then.DurationMs is not > 0)
        {
            return new ReactionSeedResult.Refused("HighlightOverlay reaction has no positive DurationMs.");
        }

        RuleSeed seed = new(
            name, reaction.When.Source, reaction.When.Kind, predicate,
            new RuleSeedAction.HighlightOverlay(overlay.Value, reaction.Then.DurationMs.Value));
        return new ReactionSeedResult.Valid(seed);
    }

    private static ReactionSeedResult FromSetVariableValue(
        string name, AssetDefinition asset, ReactionDefinition reaction, string predicate)
    {
        if (string.IsNullOrWhiteSpace(reaction.Then.Variable))
        {
            return new ReactionSeedResult.Refused("SetVariableValue reaction has no Variable.");
        }

        if (string.IsNullOrWhiteSpace(reaction.Then.Value))
        {
            return new ReactionSeedResult.Refused("SetVariableValue reaction has no Value.");
        }

        bool declared = asset.Variables.Any(variable =>
            string.Equals(variable.Name, reaction.Then.Variable, StringComparison.Ordinal));
        if (!declared)
        {
            return new ReactionSeedResult.Refused(
                $"SetVariableValue reaction names variable '{reaction.Then.Variable}', which this asset does not declare.");
        }

        RuleSeed seed = new(
            name, reaction.When.Source, reaction.When.Kind, predicate,
            new RuleSeedAction.SetVariableValue(reaction.Then.Variable, reaction.Then.Value));
        return new ReactionSeedResult.Valid(seed);
    }

    private static bool IsValidRuleName(string name)
    {
        if (name.Length < MinimumNameLength || name.Length > MaximumNameLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(name[0]))
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Whether a declared reaction produced a seedable <see cref="RuleSeed"/>.</summary>
internal abstract record ReactionSeedResult
{
    internal sealed record Valid(RuleSeed Seed) : ReactionSeedResult;

    internal sealed record Refused(string Reason) : ReactionSeedResult;
}
