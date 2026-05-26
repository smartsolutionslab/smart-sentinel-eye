using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Application.Queries;

/// <summary>
/// Read-side IQueryable seam for the Layout aggregate. Infrastructure
/// provides a concrete impl backed by the DbContext; Application stays
/// EF-Core-free at the call site so handler tests can substitute an
/// in-memory <see cref="IQueryable{T}"/>.
/// </summary>
public interface ILayoutQuerySource
{
    IQueryable<Layout> Layouts { get; }
}
