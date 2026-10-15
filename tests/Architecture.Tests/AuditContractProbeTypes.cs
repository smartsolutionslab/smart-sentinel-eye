namespace SmartSentinelEye.Architecture.Tests;

// Probe shapes for the audit contract guards that need a public field. They sit at namespace level because a
// private nested type with a public field is reported as unused, and a visible one as an exposed field.
internal sealed record ProbeLeafV1(string Address);

internal sealed class ProbeFieldV1
{
    public string Endpoint = string.Empty;
}

internal sealed class ProbeFieldHolderV1
{
    public ProbeLeafV1 Leaf = new(string.Empty);
}

internal sealed class ProbeFieldsV1
{
    public string Name = string.Empty;
    public object Loose = new();
}
