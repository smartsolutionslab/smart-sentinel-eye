using SmartSentinelEye.EventIngestion.Domain.SourceMode;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

public interface ISourceModeQuerySource
{
    IQueryable<SourceMode> SourceModes { get; }
}
