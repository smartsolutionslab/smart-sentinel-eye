using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Redaction;

/// <summary>Phase 4a scaffold (spec 449); the behaviour lands in phase 4b.</summary>
public static class StoredCameraUrlRewrite
{
    public static IReadOnlyCollection<string> EventKinds { get; } = [];

    public static IReadOnlyList<string> KeysFor(string eventKind) => [];

    public static Option<Plan> PlanFor(string eventKind, IReadOnlyDictionary<string, string?> storedValues) =>
        Option<Plan>.None;

    public sealed record Plan(IReadOnlyList<Change> Changes, int SizeDelta);

    public sealed record Change(string Key, string Old, string New);
}
