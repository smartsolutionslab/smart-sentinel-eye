using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="RuleAggregate"/> (spec 007).
///
/// <para>
/// The discriminated <see cref="RuleAction"/> VO round-trips via a
/// single packed text column (<c>action_packed</c>) using
/// <see cref="RuleActionColumnConverter"/>. Same pattern as
/// spec 005's <c>VariableValue</c>.
/// </para>
/// </summary>
public sealed class RuleConfiguration : IEntityTypeConfiguration<RuleAggregate>
{
    public void Configure(EntityTypeBuilder<RuleAggregate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rules");
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasColumnName("rule_id")
            .HasConversion(id => id.Value, value => RuleIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(rule => rule.Name)
            .HasColumnName("name")
            .HasMaxLength(RuleName.MaximumLength)
            .HasConversion(name => name.Value, value => RuleName.From(value))
            .IsRequired();

        builder.Property(rule => rule.TriggerSource)
            .HasColumnName("trigger_source")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(rule => rule.TriggerKind)
            .HasColumnName("trigger_kind")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(rule => rule.Predicate)
            .HasColumnName("predicate")
            .HasMaxLength(RulePredicate.MaximumLength)
            .HasConversion(predicate => predicate.Value, value => RulePredicate.From(value))
            .IsRequired();

        builder.Property(rule => rule.Action)
            .HasColumnName("action_packed")
            .HasColumnType("text")
            .HasConversion(
                action => RuleActionColumnConverter.ToColumn(action),
                value => RuleActionColumnConverter.FromColumn(value))
            .IsRequired();

        builder.Property(rule => rule.State)
            .HasColumnName("state")
            .HasMaxLength(16)
            .HasConversion(state => state.Value, value => RuleState.From(value))
            .IsRequired();

        builder.Property(rule => rule.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(rule => rule.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
            .IsRequired();

        builder.Property(rule => rule.PublishedAt).HasColumnName("published_at");
        builder.Property(rule => rule.ArchivedAt).HasColumnName("archived_at");

        builder.Property(rule => rule.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // FR-002 belt-and-braces: at most one non-Archived rule per
        // name. The application handler is the authoritative source
        // of truth; this partial unique index prevents drift.
        builder.HasIndex(rule => rule.Name)
            .HasDatabaseName("ux_rules_name_active")
            .IsUnique()
            .HasFilter("state <> 'Archived'");

        // Trigger lookup path used by the cache seeder.
        builder.HasIndex(rule => new { rule.TriggerSource, rule.TriggerKind, rule.State })
            .HasDatabaseName("ix_rules_trigger_state");

        builder.Ignore(rule => rule.PendingEvents);
    }
}

/// <summary>
/// Packs / unpacks a <see cref="RuleAction"/> for storage via a
/// single column. Format:
///   <c>SetVariableValue|&lt;variableName&gt;|&lt;valueExpression&gt;</c>
///   <c>HighlightOverlay|&lt;guid&gt;|&lt;durationMs&gt;</c>
/// The leading discriminator avoids cross-shape ambiguity.
/// </summary>
internal static class RuleActionColumnConverter
{
    private const string SetVariableValueTag = "SetVariableValue";
    private const string HighlightOverlayTag = "HighlightOverlay";

    public static string ToColumn(RuleAction action) =>
        action switch
        {
            RuleAction.SetVariableValue sv =>
                $"{SetVariableValueTag}|{sv.VariableName}|{sv.ValueExpression}",
            RuleAction.HighlightOverlay h =>
                $"{HighlightOverlayTag}|{h.Overlay:D}|{h.DurationMs.ToString(CultureInfo.InvariantCulture)}",
            _ => throw new InvalidOperationException($"Unhandled RuleAction case: {action.GetType().Name}"),
        };

    public static RuleAction FromColumn(string packed)
    {
        ArgumentNullException.ThrowIfNull(packed);
        int firstSep = packed.IndexOf('|', StringComparison.Ordinal);
        if (firstSep < 0)
        {
            throw new ArgumentException($"Malformed packed RuleAction '{packed}'.", nameof(packed));
        }
        string tag = packed[..firstSep];
        string remainder = packed[(firstSep + 1)..];

        return tag switch
        {
            SetVariableValueTag => ParseSetVariableValue(remainder),
            HighlightOverlayTag => ParseHighlightOverlay(remainder),
            _ => throw new ArgumentException($"Unknown RuleAction tag '{tag}'.", nameof(packed)),
        };
    }

    private static RuleAction.SetVariableValue ParseSetVariableValue(string remainder)
    {
        int sep = remainder.IndexOf('|', StringComparison.Ordinal);
        if (sep < 0)
        {
            throw new ArgumentException("SetVariableValue requires `<name>|<expression>`.", nameof(remainder));
        }
        return RuleAction.SetVariableValue.From(remainder[..sep], remainder[(sep + 1)..]);
    }

    private static RuleAction.HighlightOverlay ParseHighlightOverlay(string remainder)
    {
        int sep = remainder.IndexOf('|', StringComparison.Ordinal);
        if (sep < 0)
        {
            throw new ArgumentException("HighlightOverlay requires `<guid>|<durationMs>`.", nameof(remainder));
        }
        if (!Guid.TryParseExact(remainder[..sep], "D", out Guid overlay))
        {
            throw new ArgumentException($"Malformed overlay guid '{remainder[..sep]}'.", nameof(remainder));
        }
        if (!int.TryParse(
            remainder[(sep + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int duration))
        {
            throw new ArgumentException($"Malformed durationMs '{remainder[(sep + 1)..]}'.", nameof(remainder));
        }
        return RuleAction.HighlightOverlay.From(overlay, duration);
    }
}
