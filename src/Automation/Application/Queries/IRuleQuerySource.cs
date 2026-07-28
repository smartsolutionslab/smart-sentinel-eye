using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.Queries;

/// <summary>
/// Read-side IQueryable seam for the Rule aggregate (spec 007 T059).
/// Infrastructure supplies a DbContext-backed implementation; Application
/// stays EF-Core-free at the call site so handler tests can substitute an
/// in-memory <see cref="IQueryable{T}"/>.
/// </summary>
public interface IRuleQuerySource
{
    IQueryable<Rule> Rules { get; }
}
