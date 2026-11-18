using System;
using System.IO.MemoryMappedFiles;

namespace PvpChronicle.RecordingContract;

public sealed record RecordingRawFrame(long Sequence, int Width, int Height, byte[] Bgra);

/// <summary>
/// A small local-only frame transport. Recorder owns the mapping and Chronicle reads it from the
/// same Windows session. IPC carries only the mapping name and sequence; raw pixels never become
/// JSON/base64 and never leave this computer.
/// </summary>
public sealed class RecordingFramePublisher : IDisposable
{
    private const int HeaderBytes = 64;
    private const int Magic = 0x46564350; // PVCF
    private readonly int maxPayloadBytes;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private long sequence;

    public RecordingFramePublisher(string name, int maxWidth = 1920, int maxHeight = 1080)
    {
        Name = name;
        maxPayloadBytes = checked(maxWidth * maxHeight * 4);
        mapping = MemoryMappedFile.CreateOrOpen(name, HeaderBytes + maxPayloadBytes,
            MemoryMappedFileAccess.ReadWrite);
        view = mapping.CreateViewAccessor(0, HeaderBytes + maxPayloadBytes,
            MemoryMappedFileAccess.ReadWrite);
    }

    public string Name { get; }
    public long Sequence => sequence;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public void PublishBgra(int width, int height, ReadOnlySpan<byte> pixels)
    {
        var payloadBytes = checked(width * height * 4);
        if (width <= 0 || height <= 0 || payloadBytes > maxPayloadBytes ||
            pixels.Length < payloadBytes)
            throw new ArgumentOutOfRangeException(nameof(pixels), "Invalid BGRA frame dimensions.");
        var next = sequence + 2;
        view.Write(8, next | 1L); // odd means a writer is currently changing the buffer
        view.Write(0, Magic);
        view.Write(4, 1);
        view.Write(16, width);
        view.Write(20, height);
        view.Write(24, width * 4);
        view.Write(28, payloadBytes);
        view.WriteArray(HeaderBytes, pixels[..payloadBytes].ToArray(), 0, payloadBytes);
        view.Write(8, next); // even publishes one complete frame atomically
        Width = width;
        Height = height;
        sequence = next;
    }

    public void Dispose()
    {
        view.Dispose();
        mapping.Dispose();
    }
}

public sealed class RecordingFrameConsumer : IDisposable
{
    private const int HeaderBytes = 64;
    private const int Magic = 0x46564350;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;

    public RecordingFrameConsumer(string name)
    {
        mapping = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
        view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public bool TryRead(long newerThan, out RecordingRawFrame? frame)
    {
        frame = null;
        var before = view.ReadInt64(8);
        if (before <= newerThan || (before & 1) != 0 || view.ReadInt32(0) != Magic) return false;
        var width = view.ReadInt32(16);
        var height = view.ReadInt32(20);
        var stride = view.ReadInt32(24);
        var payloadBytes = view.ReadInt32(28);
        if (width <= 0 || height <= 0 || stride != width * 4 ||
            payloadBytes != checked(stride * height) || payloadBytes > 1920 * 1080 * 4) return false;
        var pixels = new byte[payloadBytes];
        view.ReadArray(HeaderBytes, pixels, 0, payloadBytes);
        var after = view.ReadInt64(8);
        if (after != before || (after & 1) != 0) return false;
        frame = new RecordingRawFrame(after, width, height, pixels);
        return true;
    }

    public void Dispose()
    {
        view.Dispose();
        mapping.Dispose();
    }
}
