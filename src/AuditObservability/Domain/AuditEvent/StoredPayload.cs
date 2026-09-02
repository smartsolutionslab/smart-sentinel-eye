using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// An audit row's captured body together with its size — spec 058.
///
/// <para>
/// <b>Not a pair, a derivation.</b> The size is the UTF-8 byte count of the
/// content, so the two cannot be supplied independently: <see cref="From"/>
/// takes content alone and computes the rest. Before this type they were two
/// properties set side by side, and nothing prevented a size that did not
/// describe its content.
/// </para>
///
/// <para>
/// The two-argument constructor is <b>private</b> and exists for
/// materialisation only. Every row in the table predates this type and must
/// still load; EF binds that constructor by reflection. Exposing it would put
/// the invariant one call away from being bypassed, which is the state this
/// type removes.
/// </para>
///
/// <para>
/// A row already stored with a mismatched size reconstructs with the mismatch
/// intact rather than being repaired. Repairing stored data is a migration,
/// and this feature adds none (FR-004). Whether any such row exists is
/// untested for; what this type guarantees is that no new one is written.
/// </para>
/// </summary>
public sealed record StoredPayload : IValueObject
{
    private StoredPayload(AuditPayload content, PayloadSizeBytes size)
    {
        Content = content;
        Size = size;
    }

    public AuditPayload Content { get; private init; }

    public PayloadSizeBytes Size { get; private init; }

    /// <summary>The only public way to build one: the size follows from the content.</summary>
    public static StoredPayload From(string content)
    {
        Ensure.That(content).IsNotNull();
        return new(AuditPayload.From(content), PayloadSizeBytes.Of(content));
    }
}
