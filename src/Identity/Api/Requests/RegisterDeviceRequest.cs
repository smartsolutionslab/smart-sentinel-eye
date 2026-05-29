namespace SmartSentinelEye.Identity.Api.Requests;

public sealed record RegisterDeviceRequest(string DeviceType, string DeviceIdentifier);
