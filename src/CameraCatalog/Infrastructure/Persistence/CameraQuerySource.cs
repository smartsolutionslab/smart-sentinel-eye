using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.Configurations;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

/// <summary>
/// EF-Core-backed read-side seam (ICameraQuerySource). Uses AsNoTracking to
/// keep list queries cheap.
/// </summary>
public sealed class CameraQuerySource(CameraCatalogDbContext dbContext) : ICameraQuerySource
{
    public IQueryable<Camera> Cameras => dbContext.Cameras.AsNoTracking();

    /// <summary>
    /// Matches against the generated <c>name_normalized</c> column — the same
    /// one <c>ux_cameras_fab_name_normalized_active</c> is built on, so search
    /// and uniqueness agree about when two names are the same name.
    ///
    /// <para>
    /// Reached through <c>EF.Property</c> because the column is a shadow
    /// property, and named through <see cref="CameraConfiguration"/>'s constant
    /// rather than spelled here: a typo in that string fails as "no rows", not
    /// as an error.
    /// </para>
    ///
    /// <para>
    /// <c>string.Contains</c> translates to a parameterised <c>LIKE</c> with the
    /// wildcards escaped, so the fragment stays text — a camera called
    /// <c>50% Load</c> is found by typing <c>%</c>, and a fragment of <c>%</c>
    /// does not match everything.
    /// </para>
    /// </summary>
    public Expression<Func<Camera, bool>> NameContains(string normalizedFragment) =>
        camera => EF.Property<string>(camera, CameraConfiguration.NormalizedNameProperty)
            .Contains(normalizedFragment);
}
