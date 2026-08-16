namespace SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;

/// <summary>
/// Answers "which fab does this camera belong to" for streams provisioned
/// before spec 016. The seam exists so the attribution pass can be tested
/// without Keycloak or CameraCatalog — its interesting behaviour is what it
/// does when a camera cannot be resolved (FR-010), which is unreachable
/// through a real HTTP client.
/// </summary>
public interface ICameraFabLookup
{
    /// <summary>
    /// A map from camera identifier to fab name, covering every camera the
    /// lookup can see.
    ///
    /// <para>
    /// Fetched whole rather than per camera: CameraCatalog has no read-by
    /// -identifier route (#1435), and at the 250-camera target the entire
    /// catalogue is one or two requests — fewer than resolving each stream
    /// individually would be even if that route existed.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> FabsByCameraAsync(CancellationToken cancellationToken);
}
