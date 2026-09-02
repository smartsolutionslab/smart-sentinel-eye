using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// When a stream was provisioned, and by whom. The two were separate
/// properties set in the same statement and connected by nothing — spec 058.
///
/// <para>
/// Declared here rather than shared with the other contexts that have the same
/// shape. <see cref="ProvisionedAt"/> is StreamDistribution's own timestamp
/// type, and a common composite would either reach across a context boundary
/// (§III) or take a bare <c>DateTimeOffset</c> and undo spec 057. Six
/// near-identical types elsewhere are the price, paid deliberately.
/// </para>
/// </summary>
public sealed record Provisioning(ProvisionedAt At, OperatorIdentifier By) : IValueObject
{
    public static Provisioning From(ProvisionedAt at, OperatorIdentifier by)
    {
        Ensure.That(at).IsNotNull();
        return new(at, by);
    }
}
