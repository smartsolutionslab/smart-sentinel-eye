namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// The rows of spec 414 §6. <see cref="EmptyPort"/> is what the empty-port rule (FR-001) refuses; the other sets are
/// the rows it must leave as they are. The invisible character is built from its code point, never written into the
/// source.
/// </summary>
internal static class EmptyPortRows
{
    public static readonly string[] Accepted =
    [
        "rtsp://10.0.0.66:554/live", "rtsp://10.0.0.66/live", "rtsp://cam:554/x", "rtsp://cam:0/x",
        "rtsp://cam:00/x", "rtsp://[::]:0/x", "rtsp://[fe80::1]:0554/s", "rtsp://[::1]:8554/live",
        "rtsp://cam/x:", "rtsp://cam/:/x", "rtsp://cam/x?a=:", "rtsp://cam/x#:",
        "rtsp://[1:2:3:4:5:6:7::/64]/s", "rtsp://[fe80::1/64]/x", "rtsp://cam",
    ];

    public static readonly string[] EmptyPort =
    [
        "rtsp://10.0.0.66:/live", "rtsp://[::1]:/live", "rtsp://cam:", "rtsp://cam:/x", "rtsp://cam:?q",
        "rtsp://cam:#f", "rtsp://@cam:/x", "rtsp://cam.:/x", "RTSP://CAM:/S", "rtsp://cam:/x?a=b",
        "rtsp://xn--kamera-mnchen-wob.local:/live", "rtsp://[fe80::1]:", "rtsp://[fe80::1]:/",
        "rtsp://[fe80::1]:?q", "rtsp://[fe80::1]:#f", "rtsp://[fe80::1]:/s", "rtsp://[fe80::1]:/s?x#y",
        "rtsp://[fe80::1%25eth0]:/s", "rtsp://@[::1]:/x", "rtsp://[::ffff:10.0.0.1]:/s", "rtsp://[fe80::1/64]:/x",
        "rtsp://[fe80::1/64%25eth0]:/x", "rtsp://[1:2:3:4:5:6:7::/64]:/s", LeftToRightMarkHost,
    ];

    public static readonly string[] OlderRule =
    [
        "rtsp://cam::/x", "rtsp://[::1]::/x", "rtsp://u@cam:/x", "rtsp://..:/x", "rtsp:///cam:/x",
        "rtsp://[fe80::1]x:/s", "rtsp://[1:2:3:4:5:6:7:8:/64]:/s", "rtsp://[::1%25x?y]:/s", "rtsp://cam: ",
        "rtsp://[fe80::1%25eth0%0D]:/s", "rtsp://-:/x",
    ];

    public static string LeftToRightMarkHost => "rtsp://cam" + char.ConvertFromUtf32(0x200E) + ":/p";
}
