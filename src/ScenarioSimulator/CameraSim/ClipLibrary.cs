namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>
/// Answers whether a clip a scenario names actually exists (FR-007).
///
/// <para>
/// The worker and camera-sim see the same clips at different paths — the worker
/// reads the repository directory, camera-sim has it bind-mounted at
/// <c>/media</c> — so this asks about the copy the worker can see and trusts the
/// mount to carry the same set. That is the same assumption the bind mount
/// already encodes; what it buys is the failure arriving at seed time, named,
/// instead of arriving as a tile that never goes live.
/// </para>
/// </summary>
public sealed class ClipLibrary(string directory)
{
    /// <summary>
    /// Where the clips live relative to the running worker. Empty disables the
    /// check — used where the directory is genuinely unknowable, so an
    /// unverifiable clip is provisioned rather than refused.
    /// </summary>
    public string Directory { get; } = directory;

    public bool Exists(string clip)
    {
        if (string.IsNullOrWhiteSpace(Directory) || string.IsNullOrWhiteSpace(clip))
        {
            return true;
        }

        return File.Exists(Path.Combine(Directory, clip));
    }
}
