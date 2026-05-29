namespace SmartSentinelEye.Shared.Contracts.SystemVariables;

/// <summary>
/// Integration event published when a system variable is archived
/// (terminal lifecycle transition). Subscribers should treat the
/// variable as gone; any cached value is now stale. The name is
/// released for re-use by a subsequent <c>DefineVariable</c>.
/// </summary>
public sealed record SystemVariableArchivedV1(
    Guid Variable,
    string Name,
    DateTimeOffset ArchivedAt,
    Guid ArchivedBy,
    EventMetadata Metadata) : IIntegrationEvent;
