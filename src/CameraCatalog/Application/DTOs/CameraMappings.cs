using System.Linq.Expressions;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.DTOs;

internal static class CameraMappings
{
    public static CameraDto ToDto(this Camera camera) =>
        new(
            camera.Id.Value,
            camera.Version,
            camera.Fab.Value,
            camera.Name.Value,
            UrlRedaction.ToOrigin(camera.Url.Value),
            camera.Registration.At,
            camera.Status.Value);

    public static CameraAddressDto ToAddressDto(this Camera camera) =>
        new(camera.Url.Value, camera.Version);

    // An expression, not a method: EF translates it into the SELECT, so the lambda must stay a lambda.
    public static Expression<Func<Camera, CameraSummaryDto>> ToSummaryDtoExpression() =>
        camera => new CameraSummaryDto(
            camera.Id.Value,
            camera.Version,
            camera.Fab.Value,
            camera.Name.Value,
            camera.Url.Value,
            camera.Registration.At,
            camera.Status.Value);

    public static CameraListPageDto ToCameraListPageDto(
        this IReadOnlyList<CameraSummaryDto> items,
        int count,
        int offset,
        int limit) =>
        new(items, count, offset, limit);
}
