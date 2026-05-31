using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Variable"/> aggregate (spec 005).
///
/// <para>
/// The discriminated <see cref="VariableValue"/> VO round-trips via a
/// single packed column (<c>kind|wire</c>) using
/// <see cref="VariableValueColumnConverter"/>. Cheap to write, cheap
/// to read, and the kiosk path never queries it directly — the
/// reverse-index works off the in-memory variable repository.
/// </para>
///
/// <para>
/// Name uniqueness is enforced at the application layer (FR-005;
/// archived names are released for re-use). A partial unique index on
/// non-archived rows backs it belt-and-braces.
/// </para>
/// </summary>
public sealed class VariableConfiguration : IEntityTypeConfiguration<Variable>
{
    public void Configure(EntityTypeBuilder<Variable> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("system_variables");
        builder.HasKey(variable => variable.Id);

        builder.Property(variable => variable.Id)
            .HasColumnName("variable_id")
            .HasConversion(id => id.Value, value => VariableIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(variable => variable.Name)
            .HasColumnName("name")
            .HasMaxLength(VariableName.MaximumLength)
            .HasConversion(name => name.Value, value => VariableName.From(value))
            .IsRequired();

        builder.Property(variable => variable.Type)
            .HasColumnName("type")
            .HasMaxLength(16)
            .HasConversion(type => type.Value, value => VariableType.From(value))
            .IsRequired();

        builder.Property(variable => variable.State)
            .HasColumnName("state")
            .HasMaxLength(16)
            .HasConversion(state => state.Value, value => VariableState.From(value))
            .IsRequired();

        builder.Property(variable => variable.Value)
            .HasColumnName("value_packed")
            .HasMaxLength(512)
            .HasConversion(
                value => VariableValueColumnConverter.ToColumn(value),
                packed => VariableValueColumnConverter.FromColumn(packed))
            .IsRequired();

        builder.OwnsOne(variable => variable.BooleanLabels, labels =>
        {
            labels.Property(label => label.TruthyLabel)
                .HasColumnName("truthy_label")
                .HasMaxLength(BooleanLabels.MaximumLength);
            labels.Property(label => label.FalsyLabel)
                .HasColumnName("falsy_label")
                .HasMaxLength(BooleanLabels.MaximumLength);
        });

        builder.Property(variable => variable.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(variable => variable.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
            .IsRequired();

        builder.Property(variable => variable.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // FR-005 belt-and-braces: at most one non-Archived variable
        // per name. The application-level uniqueness check is the
        // authoritative source of truth.
        builder.HasIndex(variable => variable.Name)
            .HasDatabaseName("ux_system_variables_name_active")
            .IsUnique()
            .HasFilter("state <> 'Archived'");

        builder.Ignore(variable => variable.PendingEvents);
    }
}

/// <summary>
/// Packs / unpacks a <see cref="VariableValue"/> for storage via a
/// single column. Format: <c>"Kind|WireValue"</c>; e.g.
/// <c>"String|hello"</c>, <c>"Number|82.4"</c>, <c>"Boolean|true"</c>,
/// <c>"Unset|"</c>. Round-trip-safe with the wire-string per FR-007.
/// </summary>
internal static class VariableValueColumnConverter
{
    public static string ToColumn(VariableValue value) =>
        value switch
        {
            VariableValue.Unset => "Unset|",
            VariableValue.StringValue stringValue => "String|" + stringValue.Value,
            VariableValue.NumberValue numberValue => "Number|" + numberValue.ToWireString(),
            VariableValue.BooleanValue booleanValue => "Boolean|" + (booleanValue.Value ? "true" : "false"),
            _ => throw new InvalidOperationException("Unreachable VariableValue case."),
        };

    public static VariableValue FromColumn(string packed)
    {
        ArgumentNullException.ThrowIfNull(packed);
        int sep = packed.IndexOf('|', StringComparison.Ordinal);
        if (sep < 0) throw new ArgumentException($"Malformed packed VariableValue '{packed}'.", nameof(packed));
        string kind = packed[..sep];
        string wire = packed[(sep + 1)..];
        return kind switch
        {
            "Unset" => VariableValue.Unset.Instance,
            "String" => new VariableValue.StringValue(wire),
            "Number" => VariableValue.From(VariableType.Number, wire),
            "Boolean" => VariableValue.From(VariableType.Boolean, wire),
            _ => throw new ArgumentException($"Unknown packed VariableValue kind '{kind}'.", nameof(packed)),
        };
    }
}
