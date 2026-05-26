using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

/// <summary>
/// EF-Core-backed read-side seam (ICameraQuerySource). Uses AsNoTracking to
/// keep list queries cheap.
/// </summary>
public sealed class CameraQuerySource(CameraCatalogDbContext dbContext) : ICameraQuerySource
{
    public IQueryable<Camera> Cameras => dbContext.Cameras.AsNoTracking();
}
