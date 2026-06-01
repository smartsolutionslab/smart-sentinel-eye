namespace SmartSentinelEye.StreamDistribution.Infrastructure.Auth;

public sealed class WhepAuthOptions
{
    public const string SectionName = "WhepAuth";
    public string Authority { get; set; } = string.Empty;
}
