using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

/// <summary>
/// Read-side seam: hands query handlers an EF Core <see cref="IQueryable{T}"/>
/// over the Layouts table with the owned <c>Revisions</c> collection
/// pre-included. <c>AsNoTracking</c> by default since query handlers do
/// not mutate the aggregate.
/// </summary>
public sealed class LayoutQuerySource(LayoutCompositionDbContext dbContext) : ILayoutQuerySource
{
    public IQueryable<Layout> Layouts =>
        dbContext.Layouts.AsNoTracking();
}
