using SmartSentinelEye.EventIngestion.Domain.SourceMode;

namespace SmartSentinelEye.EventIngestion.Application.DTOs;

internal static class SourceModeMappings
{
    public static SourceModeDto ToDto(this SourceMode sourceMode) =>
        new(
            sourceMode.Id.Value,
            sourceMode.Fab.Value,
            sourceMode.Source.Value,
            sourceMode.Mode.Value,
            sourceMode.Declaration.DeclaredAt,
            sourceMode.Declaration.DeclaredBy,
            sourceMode.Version);
}
