using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.OverlayDesigner.Application.Queries;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;

/// <summary>
/// Read-side seam: hands query handlers an EF Core <see cref="IQueryable{T}"/>
/// over the Overlays table with the owned <c>Revisions</c> collection
/// pre-included. <c>AsNoTracking</c> by default.
/// </summary>
public sealed class OverlayQuerySource(OverlayDesignerDbContext dbContext) : IOverlayQuerySource
{
    public IQueryable<Overlay> Overlays =>
        dbContext.Overlays.AsNoTracking();
}
