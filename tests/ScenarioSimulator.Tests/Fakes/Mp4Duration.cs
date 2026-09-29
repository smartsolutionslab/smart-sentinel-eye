using System.Buffers.Binary;
using System.Text;

namespace SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

/// <summary>
/// Reads an MP4's duration straight from its <c>moov/mvhd</c> box (spec 289 /
/// PR-B, T-B09 — "no ffprobe dependency in unit tests", tasks.md). Hand-rolled
/// ISO/IEC 14496-12 box walking (ADR-0054): a box is an 8-byte header
/// (<c>uint32</c> big-endian size, 4-character type), a 64-bit
/// "largesize" when the 32-bit size reads <c>1</c>, or "extends to end of
/// file" when it reads <c>0</c>. <c>moov</c> is a pure container — its
/// children start immediately after its own header, with no extra fields —
/// so finding <c>mvhd</c> is: walk the top level for <c>moov</c>, then walk
/// <c>moov</c>'s own children for <c>mvhd</c>.
///
/// <para>
/// <c>mvhd</c>'s <c>duration</c> is in units of its own <c>timescale</c>
/// (e.g. <c>duration=20000</c> at <c>timescale=1000</c> is 20 real seconds).
/// Version 1 widens <c>duration</c> (and the two timestamps before
/// <c>timescale</c>) to 64 bits; version 0 is 32-bit throughout. Verified
/// against the real, shipped <c>mill-roughing.mp4</c>
/// (<c>version=0, timescale=1000, duration=20000</c> -&gt; 20000ms, matching
/// the sidecar's declared <c>DurationMs</c> and the reviewer's independent
/// measurement) before being wired into <c>ScenarioFileTests</c>.
/// </para>
/// </summary>
internal static class Mp4Duration
{
    /// <summary>The clip's duration in milliseconds, or <c>null</c> if it has no <c>mvhd</c> box.</summary>
    public static double? ReadMs(string mp4Path)
    {
        using FileStream stream = File.OpenRead(mp4Path);
        return ReadMvhd(stream, 0, stream.Length);
    }

    private static double? ReadMvhd(FileStream stream, long start, long end)
    {
        long position = start;

        while (position < end)
        {
            stream.Seek(position, SeekOrigin.Begin);
            long boxSize = ReadUInt32(stream);
            string boxType = ReadFourCharacterCode(stream);
            long headerSize = 8;

            if (boxSize == 1)
            {
                boxSize = (long)ReadUInt64(stream);
                headerSize = 16;
            }
            else if (boxSize == 0)
            {
                boxSize = end - position;
            }

            if (boxSize < headerSize)
            {
                // A malformed or truncated box; nothing sane to descend into.
                return null;
            }

            long dataStart = position + headerSize;
            long dataEnd = position + boxSize;

            if (boxType == "moov")
            {
                double? found = ReadMvhd(stream, dataStart, dataEnd);
                if (found is not null)
                {
                    return found;
                }
            }
            else if (boxType == "mvhd")
            {
                return ReadMovieHeader(stream, dataStart);
            }

            position += boxSize;
        }

        return null;
    }

    private static double? ReadMovieHeader(FileStream stream, long dataStart)
    {
        stream.Seek(dataStart, SeekOrigin.Begin);
        int version = stream.ReadByte();
        stream.Seek(3, SeekOrigin.Current); // flags

        long timescale;
        long duration;

        if (version == 1)
        {
            stream.Seek(16, SeekOrigin.Current); // creation_time + modification_time, 8 bytes each
            timescale = ReadUInt32(stream);
            duration = (long)ReadUInt64(stream);
        }
        else
        {
            stream.Seek(8, SeekOrigin.Current); // creation_time + modification_time, 4 bytes each
            timescale = ReadUInt32(stream);
            duration = ReadUInt32(stream);
        }

        return timescale <= 0 ? null : duration * 1000.0 / timescale;
    }

    private static uint ReadUInt32(FileStream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    private static ulong ReadUInt64(FileStream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private static string ReadFourCharacterCode(FileStream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return Encoding.ASCII.GetString(buffer);
    }
}
