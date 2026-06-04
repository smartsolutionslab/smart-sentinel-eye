namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>One resolved tile of the rolling-mill wall: a camera + its overlay at a grid position.</summary>
public sealed record CorrelatedTile(Guid Camera, Guid Overlay, int Row, int Col);
