using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Application.DTOs;

internal static class WallMappings
{
    public static WallDto ToDto(this Wall wall) =>
        new(
            Wall: wall.Id.Value,
            Version: wall.Version,
            Fab: wall.Fab.Value,
            Name: wall.Name.Value,
            Scenes: [.. wall.Scenes.Select(scene => scene.Value)],
            Showing: wall.Showing.Value,
            SceneVersion: wall.SceneVersion.Value,
            ShowingSince: wall.ShowingSince.Value);
}
