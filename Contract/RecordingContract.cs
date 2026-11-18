using System;
using System.Text.Json;

namespace PvpChronicle.RecordingContract;

/// <summary>
/// Versioned JSON contract shared as source by both plugins.  IPC transports only strings so
/// neither plugin takes a runtime assembly dependency on the other's private implementation.
/// </summary>
public static class RecordingContract
{
    public const string Schema = "pvpchronicle-recording/v1";
    public const string MatchEventChannel = "PvpChronicle.Recording.MatchEvent.V1";
    public const string AttachRecordingChannel = "PvpChronicle.Recording.Attach.V1";
    public const string OpenAtChannel = "PvpChronicle.Recording.OpenAt.V1";
    public const string ChronicleInfoChannel = "PvpChronicle.Recording.Info.V1";
    public const string PlaybackControlChannel = "PvpChronicle.Recording.Playback.Control.V1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

public sealed record MatchRecordingEvent(
    string Schema,
    string Kind,
    string MatchUid,
    string Mode,
    uint DutyId,
    uint TerritoryId,
    DateTimeOffset OccurredAtUtc,
    long ElapsedMs,
    string? DataJson = null);

public sealed record RecordingAttachment(
    string Schema,
    string MatchUid,
    string ProviderId,
    string VideoPath,
    DateTimeOffset MatchStartedAtUtc,
    DateTimeOffset RecordingStartedAtUtc,
    DateTimeOffset? RecordingEndedAtUtc,
    long FirstFrameCalibrationMs = 0,
    long? DurationMs = null);

/// <summary>
/// Small, versioned playback interface exposed by the optional recorder plugin.  Chronicle sends
/// only intent and the rectangle reserved by its replay page; it never loads Flyleaf or FFmpeg.
/// </summary>
public sealed record RecordingPlaybackRequest(
    string Schema,
    string Command,
    string MatchUid,
    long MatchElapsedMs = 0,
    int X = 0,
    int Y = 0,
    int Width = 0,
    int Height = 0,
    bool Visible = false,
    double Speed = 1,
    int PreferredFrameWidth = 1280);

/// <summary>
/// Safe playback snapshot returned to Chronicle.  Video paths stay inside Recorder; the main
/// plugin only receives timing and availability information needed to synchronize its replay.
/// </summary>
public sealed record RecordingPlaybackState(
    string Schema,
    string MatchUid,
    bool RecorderAvailable,
    bool RecordingAttached,
    bool Loaded,
    bool Playing,
    long VideoElapsedMs,
    long MatchElapsedMs,
    long DurationMs,
    double Speed,
    string Status,
    string? Error = null,
    string? FrameMapName = null,
    long FrameSequence = 0,
    int FrameWidth = 0,
    int FrameHeight = 0);

public static class RecordingTimeMap
{
    public static long ToVideoMs(RecordingAttachment attachment, long matchElapsedMs)
    {
        var eventUtc = attachment.MatchStartedAtUtc.AddMilliseconds(Math.Max(0, matchElapsedMs));
        var mapped = (long)(eventUtc - attachment.RecordingStartedAtUtc).TotalMilliseconds +
                     attachment.FirstFrameCalibrationMs;
        if (attachment.DurationMs is long duration)
            return Math.Clamp(mapped, 0, Math.Max(0, duration));
        return Math.Max(0, mapped);
    }

    public static long ToMatchMs(RecordingAttachment attachment, long videoElapsedMs)
    {
        var recordingOffsetMs = (long)(attachment.RecordingStartedAtUtc -
                                       attachment.MatchStartedAtUtc).TotalMilliseconds;
        var mapped = Math.Max(0, videoElapsedMs) + recordingOffsetMs -
                     attachment.FirstFrameCalibrationMs;
        return Math.Max(0, mapped);
    }
}
