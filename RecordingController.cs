using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using PvpChronicle.RecordingContract;

namespace PvpChronicleRecorder;

internal sealed class RecordingController : IDisposable
{
    private sealed record LocalizedStatus(string Key, object[] Arguments);

    private readonly Configuration configuration;
    private readonly RecordingSessionStore sessions;
    private readonly ActiveRecordingStore activeRecording;
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, bool> matchEvents;
    private readonly ICallGateSubscriber<string, bool> attachRecording;
    private readonly ICallGateSubscriber<string> chronicleInfo;
    private readonly ICallGateProvider<string, long, string> openAt;
    private readonly ICallGateProvider<string, string> playbackControl;
    private readonly FlyleafPlaybackController playback;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private ObsWebSocketAdapter? obs;
    private MatchRecordingEvent? activeMatch;
    private DateTimeOffset recordingStartedAtUtc;
    private bool disposed;
    private LocalizedStatus status = new("waitingChronicle", []);

    public string Status
    {
        get
        {
            var value = Volatile.Read(ref status);
            return RecorderLocalization.F(value.Key, value.Arguments);
        }
    }
    public string? ActiveMatchUid => activeMatch?.MatchUid;

    public RecordingController(IDalamudPluginInterface pi, Configuration configuration,
        RecordingSessionStore sessions, ActiveRecordingStore activeRecording, IPluginLog log)
    {
        this.configuration = configuration;
        this.sessions = sessions;
        this.activeRecording = activeRecording;
        this.log = log;
        matchEvents = pi.GetIpcSubscriber<string, bool>(RecordingContract.MatchEventChannel);
        attachRecording = pi.GetIpcSubscriber<string, bool>(RecordingContract.AttachRecordingChannel);
        chronicleInfo = pi.GetIpcSubscriber<string>(RecordingContract.ChronicleInfoChannel);
        openAt = pi.GetIpcProvider<string, long, string>(RecordingContract.OpenAtChannel);
        playbackControl = pi.GetIpcProvider<string, string>(RecordingContract.PlaybackControlChannel);
        playback = new FlyleafPlaybackController(configuration, sessions, log);
        matchEvents.Subscribe(OnMatchEventJson);
        openAt.RegisterFunc(OpenAt);
        playbackControl.RegisterFunc(HandlePlayback);
        var recovered = activeRecording.Load();
        if (recovered != null)
        {
            activeMatch = recovered.Match;
            recordingStartedAtUtc = recovered.RecordingStartedAtUtc;
            SetStatus("recovered", ShortUid(activeMatch.MatchUid));
            log.Information("Recovered active OBS recording for match {MatchUid}", activeMatch.MatchUid);
        }
        try
        {
            using var info = JsonDocument.Parse(chronicleInfo.InvokeFunc());
            if (recovered == null)
                SetStatus(info.RootElement.TryGetProperty("available", out var available) && available.GetBoolean()
                    ? "connectedWaiting" : "chronicleUnavailable");
        }
        catch
        {
            if (recovered == null) SetStatus("chronicleMissing");
        }
        foreach (var attachment in sessions.Snapshot())
        {
            try
            {
                attachRecording.InvokeFunc(JsonSerializer.Serialize(attachment, RecordingContract.Json));
            }
            catch
            {
                // Chronicle can be temporarily unavailable during plugin reload; the recording
                // remains indexed locally and will be offered again on the next recorder load.
                break;
            }
        }
    }

    public async Task<string> TestObsAsync()
    {
        await operationGate.WaitAsync();
        try
        {
            await ResetObsAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await obs!.ConnectAsync(timeout.Token);
            SetStatus("obsConnected");
            return Status;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "OBS WebSocket connection test failed");
            SetStatus("obsFailed", ex.Message);
            return Status;
        }
        finally { operationGate.Release(); }
    }

    private void OnMatchEventJson(string json)
    {
        if (disposed) return;
        MatchRecordingEvent? value;
        try { value = JsonSerializer.Deserialize<MatchRecordingEvent>(json, RecordingContract.Json); }
        catch (Exception ex) { log.Warning(ex, "Ignored malformed Chronicle recording event"); return; }
        if (value == null || value.Schema != RecordingContract.Schema) return;
        _ = Task.Run(() => HandleAsync(value));
    }

    private async Task HandleAsync(MatchRecordingEvent value)
    {
        await operationGate.WaitAsync();
        try
        {
            if (value.Kind == "match_started") await StartAsync(value);
            else if (value.Kind == "match_ended") await StopAsync(value);
        }
        catch (Exception ex)
        {
            SetStatus("recordingError", ex.Message);
            log.Error(ex, "Recording operation failed for match {MatchUid}", value.MatchUid);
        }
        finally { operationGate.Release(); }
    }

    private async Task StartAsync(MatchRecordingEvent value)
    {
        if (!configuration.AutoRecord)
        {
            SetStatus("autoOff");
            return;
        }
        if (activeMatch != null)
        {
            await ResetObsAsync();
            using var statusTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (await obs!.IsRecordingAsync(statusTimeout.Token))
            {
                log.Warning("Ignored overlapping match start {MatchUid}; {ActiveMatchUid} is still recording",
                    value.MatchUid, activeMatch.MatchUid);
                return;
            }
            log.Warning("Discarded stale active recording recovery for {ActiveMatchUid}", activeMatch.MatchUid);
            activeMatch = null;
            activeRecording.Clear();
        }
        await ResetObsAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await obs!.StartRecordingAsync(timeout.Token);
        recordingStartedAtUtc = DateTimeOffset.UtcNow;
        activeMatch = value;
        activeRecording.Save(value, recordingStartedAtUtc);
        SetStatus("recording", value.Mode, ShortUid(value.MatchUid));
    }

    private async Task StopAsync(MatchRecordingEvent value)
    {
        if (activeMatch == null || activeMatch.MatchUid != value.MatchUid) return;
        if (obs == null) await ResetObsAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await obs!.StopRecordingAsync(timeout.Token);
        var endedAt = DateTimeOffset.UtcNow;
        var outputPath = response.TryGetProperty("outputPath", out var pathValue)
            ? pathValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("OBS did not return the recorded video path.");
        var attachment = new RecordingAttachment(RecordingContract.Schema, value.MatchUid, "obs-websocket-v5",
            Path.GetFullPath(outputPath), activeMatch.OccurredAtUtc, recordingStartedAtUtc, endedAt,
            configuration.FirstFrameCalibrationMs,
            Math.Max(0, (long)(endedAt - recordingStartedAtUtc).TotalMilliseconds));
        sessions.Save(attachment);
        try
        {
            attachRecording.InvokeFunc(JsonSerializer.Serialize(attachment, RecordingContract.Json));
        }
        catch (Exception ex)
        {
            // The local session remains recoverable if Chronicle was reloading at this exact moment.
            log.Warning(ex, "Recording was saved locally but Chronicle attachment IPC was unavailable");
        }
        activeMatch = null;
        activeRecording.Clear();
        SetStatus("saved");
    }

    private string OpenAt(string matchUid, long matchElapsedMs)
    {
        var attachment = sessions.Find(matchUid);
        if (attachment == null) return "recording_not_found";
        return PlaybackLauncher.Open(attachment.VideoPath,
            RecordingTimeMap.ToVideoMs(attachment, matchElapsedMs), configuration.VideoPlayerPath);
    }

    private string HandlePlayback(string json)
    {
        RecordingPlaybackRequest? request;
        try { request = JsonSerializer.Deserialize<RecordingPlaybackRequest>(json, RecordingContract.Json); }
        catch (Exception ex)
        {
            log.Warning(ex, "Ignored malformed Chronicle playback request");
            request = null;
        }
        var state = request == null || request.Schema != RecordingContract.Schema
            ? new RecordingPlaybackState(RecordingContract.Schema, string.Empty, true, false,
                false, false, 0, 0, 0, 1, "invalid_request")
            : playback.Control(request);
        return JsonSerializer.Serialize(state, RecordingContract.Json);
    }

    private async Task ResetObsAsync()
    {
        if (obs != null) await obs.DisposeAsync();
        obs = new ObsWebSocketAdapter(configuration.ObsWebSocketUrl, configuration.GetObsPassword());
    }

    private void SetStatus(string key, params object[] arguments) =>
        Volatile.Write(ref status, new LocalizedStatus(key, arguments));

    private static string ShortUid(string value) => value[..Math.Min(8, value.Length)];

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        matchEvents.Unsubscribe(OnMatchEventJson);
        openAt.UnregisterFunc();
        playbackControl.UnregisterFunc();
        playback.Dispose();
        operationGate.Wait();
        try { obs?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        finally { operationGate.Release(); operationGate.Dispose(); }
    }
}
