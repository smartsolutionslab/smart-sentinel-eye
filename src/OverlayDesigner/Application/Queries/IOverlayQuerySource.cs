using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries;

/// <summary>
/// Read-side IQueryable seam for the Overlay aggregate. Infrastructure
/// provides a concrete impl backed by the DbContext; Application stays
/// EF-Core-free at the call site so handler tests can substitute an
/// in-memory <see cref="IQueryable{T}"/>.
/// </summary>
public interface IOverlayQuerySource
{
    IQueryable<Overlay> Overlays { get; }
}
