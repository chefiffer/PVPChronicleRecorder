using System;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using Dalamud.Plugin.Services;
using FlyleafLib;
using FlyleafLib.Controls.WinForms;
using FlyleafLib.MediaPlayer;
using PvpChronicle.RecordingContract;

namespace PvpChronicleRecorder;

/// <summary>
/// Owns Flyleaf and its native video surface.  Chronicle only sends small JSON commands over IPC;
/// FFmpeg, Direct3D resources and the helper HWND remain entirely inside the recorder plugin.
/// </summary>
internal sealed class FlyleafPlaybackController : IDisposable
{
    private readonly Configuration configuration;
    private readonly RecordingSessionStore sessions;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private Thread? uiThread;
    private Dispatcher? dispatcher;
    private RecordingPlaybackRequest? pendingRequest;
    private RecordingAttachment? activeAttachment;
    private PlaybackForm? surface;
    private FlyleafHost? host;
    private Player? player;
    private RecordingFramePublisher? framePublisher;
    private DispatcherTimer? stateTimer;
    private DispatcherTimer? seekSettleTimer;
    private DateTimeOffset lastFrameFailureLogUtc;
    private bool firstFramePublishedLogged;
    private bool disposed;
    private bool loaded;
    private string loadedMatchUid = string.Empty;
    private long pendingSeekVideoMs;
    private bool seekSettling;
    private bool playAfterSeek;
    private bool playbackRequestedVisible;
    private int requestedFrameWidth = 1280;
    private long lastPublishedVideoMs = -1;
    private string? startupFailure;
    private RecordingPlaybackState state = new(RecordingContract.Schema, string.Empty,
        true, false, false, false, 0, 0, 0, 1, "idle");

    public FlyleafPlaybackController(Configuration configuration, RecordingSessionStore sessions,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.sessions = sessions;
        this.log = log;
    }

    public RecordingPlaybackState Control(RecordingPlaybackRequest request)
    {
        var attachment = sessions.Find(request.MatchUid);
        if (attachment == null)
            return new RecordingPlaybackState(RecordingContract.Schema, request.MatchUid, true,
                false, false, false, 0, 0, 0, 1, "recording_not_found");
        if (!File.Exists(attachment.VideoPath))
            return new RecordingPlaybackState(RecordingContract.Schema, request.MatchUid, true,
                true, false, false, 0, 0, attachment.DurationMs ?? 0, 1,
                "recording_file_missing", "录像文件已被移动或删除");

        lock (gate)
        {
            if (disposed) return state with { Status = "recorder_disposed" };
            var failed = Volatile.Read(ref startupFailure);
            if (failed != null)
                return state with { Status = "flyleaf_failed", Error = failed, Loaded = false };
            pendingRequest = request;
            activeAttachment = attachment;
            EnsureThreadLocked();
            dispatcher?.BeginInvoke(DispatcherPriority.Send, ApplyPendingRequest);
            return state with
            {
                MatchUid = request.MatchUid,
                RecorderAvailable = true,
                RecordingAttached = true
            };
        }
    }

    private void EnsureThreadLocked()
    {
        if (uiThread is { IsAlive: true }) return;
        state = state with { Status = "flyleaf_starting", Error = null };
        uiThread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = FlyleafEngineBootstrap.PlaybackThreadName
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
    }

    private void ThreadMain()
    {
        try
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            var ffmpegPath = ResolveFfmpegPath();
            if (!Directory.Exists(ffmpegPath))
                throw new DirectoryNotFoundException(
                    $"Flyleaf FFmpeg 运行库目录不存在：{ffmpegPath}");

            FlyleafEngineBootstrap.Start(new EngineConfig
            {
                FFmpegPath = ffmpegPath,
                // Recorder owns its 100 ms state timer. Disabling Flyleaf's global UI refresh
                // thread lets Dalamud unload or update this companion plugin cleanly.
                UIRefresh = false,
                LogLevel = LogLevel.Quiet,
                FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Quiet
            });

            var playerConfig = new Config();
            playerConfig.Player.AutoPlay = false;
            playerConfig.Player.SeekAccurate = true;
            playerConfig.Player.Stats = true;
            player = new Player(playerConfig);
            player.OpenCompleted += (_, args) =>
            {
                loaded = args.Success;
                if (args.Success)
                {
                    // OpenCompleted fires before CanPlay and the paused-seek worker have fully
                    // settled.  Queue the initial seek and do not allow Play to race it.
                    dispatcher.BeginInvoke(DispatcherPriority.Background,
                        () => BeginSettledSeek(pendingSeekVideoMs, false));
                }
                else
                {
                    state = state with { Status = "open_failed", Error = args.Error, Loaded = false };
                }
                RefreshState();
            };

            surface = new PlaybackForm();
            host = new FlyleafHost
            {
                Dock = DockStyle.Fill,
                DragMove = false,
                KeyBindings = false,
                PanMoveOnCtrl = false,
                PanZoomOnCtrlWheel = false,
                PanRotateOnShiftWheel = false,
                ToggleFullScreenOnDoubleClick = false,
                Player = player
            };
            surface.Controls.Add(host);
            // Flyleaf still needs a renderer HWND to produce frames, but it is now a tiny offscreen
            // decoder surface. The actual picture is transported into Chronicle as an ImGui texture.
            surface.Left = -32_000;
            surface.Top = -32_000;
            surface.Width = 32;
            surface.Height = 18;
            surface.Show();
            framePublisher = new RecordingFramePublisher(
                $"PvpChronicleRecorder.Frame.{Environment.ProcessId}.{Guid.NewGuid():N}");
            log.Information("Flyleaf frame transport created: {FrameMap}", framePublisher.Name);

            stateTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100),
                DispatcherPriority.Background, (_, _) =>
                {
                    RefreshState();
                    PublishCurrentFrame();
                }, dispatcher);
            stateTimer.Start();
            state = state with { Status = "ready", Error = null };
            ApplyPendingRequest();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Flyleaf playback host failed");
            var error = ex.GetBaseException().Message;
            Volatile.Write(ref startupFailure, error);
            state = state with { Status = "flyleaf_failed", Error = error, Loaded = false };
        }
        finally
        {
            try { stateTimer?.Stop(); } catch { }
            try { seekSettleTimer?.Stop(); } catch { }
            try { framePublisher?.Dispose(); } catch { }
            try { player?.Dispose(); } catch { }
            try { surface?.Close(); } catch { }
            framePublisher = null;
            dispatcher = null;
        }
    }

    private void ApplyPendingRequest()
    {
        RecordingPlaybackRequest? request;
        RecordingAttachment? attachment;
        lock (gate)
        {
            request = pendingRequest;
            attachment = activeAttachment;
            pendingRequest = null;
        }
        if (request == null || attachment == null || player == null || surface == null) return;

        try
        {
            if (request.Command is "hide" || !request.Visible)
            {
                playbackRequestedVisible = false;
                playAfterSeek = false;
                if (loaded && player.IsPlaying) player.Pause();
                RefreshState();
                return;
            }

            playbackRequestedVisible = true;
            var nextFrameWidth = Math.Clamp(request.PreferredFrameWidth, 640, 1920);
            if (nextFrameWidth != requestedFrameWidth)
            {
                requestedFrameWidth = nextFrameWidth;
                // A paused video does not advance CurTime. Force one fresh snapshot so changing
                // quality takes effect immediately instead of waiting for playback to resume.
                lastPublishedVideoMs = -1;
            }
            var targetVideoMs = RecordingTimeMap.ToVideoMs(attachment, request.MatchElapsedMs);
            if (!string.Equals(loadedMatchUid, request.MatchUid, StringComparison.OrdinalIgnoreCase))
            {
                seekSettleTimer?.Stop();
                seekSettling = false;
                playAfterSeek = false;
                loaded = false;
                loadedMatchUid = request.MatchUid;
                pendingSeekVideoMs = targetVideoMs;
                state = state with { Status = "opening", Loaded = false, Error = null };
                player.OpenAsync(attachment.VideoPath);
            }

            switch (request.Command)
            {
                case "seek":
                case "activate_seek":
                    pendingSeekVideoMs = targetVideoMs;
                    if (loaded)
                    {
                        var resume = player.IsPlaying;
                        if (resume) player.Pause();
                        BeginSettledSeek(targetVideoMs, resume);
                    }
                    break;
                case "play":
                    if (loaded)
                    {
                        if (seekSettling) playAfterSeek = true;
                        else player.Play();
                    }
                    else playAfterSeek = true;
                    break;
                case "pause":
                    playAfterSeek = false;
                    if (loaded) player.Pause();
                    break;
                case "toggle":
                    if (loaded) player.TogglePlayPause();
                    else playAfterSeek = !playAfterSeek;
                    break;
                case "speed":
                    if (loaded) player.Speed = Math.Clamp(request.Speed, .25, 4);
                    break;
            }
            RefreshState();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Flyleaf playback command {Command} failed", request.Command);
            state = state with { Status = "command_failed", Error = ex.Message };
        }
    }

    /// <summary>
    /// Flyleaf performs paused seeking on a background worker. Starting playback in the same
    /// dispatch turn races that worker and leaves the renderer without a presented frame. Wait
    /// for CanPlay, issue the seek, then give the worker a short settling window before resuming.
    /// </summary>
    private void BeginSettledSeek(long targetVideoMs, bool resumeAfterSeek)
    {
        var currentPlayer = player;
        var currentDispatcher = dispatcher;
        if (currentPlayer == null || currentDispatcher == null) return;

        seekSettleTimer?.Stop();
        lastPublishedVideoMs = -1;
        seekSettling = true;
        playAfterSeek |= resumeAfterSeek;
        pendingSeekVideoMs = targetVideoMs;
        var started = DateTimeOffset.UtcNow;
        var seekIssued = false;
        state = state with { Status = "seeking", Error = null, Loaded = true };
        seekSettleTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Background, (_, _) =>
            {
                try
                {
                    if (!seekIssued)
                    {
                        if (!currentPlayer.CanPlay)
                        {
                            if (DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5)) return;
                            throw new TimeoutException("视频已经打开，但播放器未进入可跳转状态");
                        }
                        currentPlayer.SeekAccurate(ClampToInt(targetVideoMs));
                        seekIssued = true;
                        started = DateTimeOffset.UtcNow;
                        return;
                    }

                    // CurTime is assigned when the seek is queued, so it cannot alone prove the
                    // worker has finished. The real-video regression probe established that a
                    // 500 ms settling window is sufficient for the OBS MKV path used here.
                    if (DateTimeOffset.UtcNow - started < TimeSpan.FromMilliseconds(500)) return;
                    seekSettleTimer?.Stop();
                    seekSettling = false;
                    state = state with { Status = "ready", Error = null, Loaded = true };
                    if (playAfterSeek)
                    {
                        playAfterSeek = false;
                        currentPlayer.Play();
                    }
                    RefreshState();
                }
                catch (Exception ex)
                {
                    seekSettleTimer?.Stop();
                    seekSettling = false;
                    playAfterSeek = false;
                    log.Warning(ex, "Flyleaf seek failed for recording");
                    state = state with
                    {
                        Status = "seek_failed", Error = ex.Message, Loaded = false
                    };
                }
            }, currentDispatcher);
        seekSettleTimer.Start();
    }

    private void RefreshState()
    {
        var attachment = activeAttachment;
        var currentPlayer = player;
        if (attachment == null || currentPlayer == null) return;
        var videoMs = Math.Max(0, currentPlayer.CurTime / TimeSpan.TicksPerMillisecond);
        var durationMs = Math.Max(attachment.DurationMs ?? 0,
            currentPlayer.Duration / TimeSpan.TicksPerMillisecond);
        state = state with
        {
            MatchUid = attachment.MatchUid,
            RecorderAvailable = true,
            RecordingAttached = true,
            Loaded = loaded,
            Playing = loaded && currentPlayer.IsPlaying,
            VideoElapsedMs = videoMs,
            MatchElapsedMs = RecordingTimeMap.ToMatchMs(attachment, videoMs),
            DurationMs = durationMs,
            Speed = loaded ? currentPlayer.Speed : 1,
            Error = loaded ? null : state.Error,
            FrameMapName = framePublisher?.Name,
            FrameSequence = framePublisher?.Sequence ?? 0,
            FrameWidth = framePublisher?.Width ?? 0,
            FrameHeight = framePublisher?.Height ?? 0
        };
    }

    private void PublishCurrentFrame()
    {
        var currentPlayer = player;
        var publisher = framePublisher;
        if (!playbackRequestedVisible || !loaded || currentPlayer == null || publisher == null ||
            !currentPlayer.Video.IsOpened) return;
        var videoMs = Math.Max(0, currentPlayer.CurTime / TimeSpan.TicksPerMillisecond);
        if (videoMs == lastPublishedVideoMs && publisher.Sequence > 0) return;
        try
        {
            using var bitmap = currentPlayer.TakeSnapshotToBitmap((uint)requestedFrameWidth, 0);
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var rowBytes = bitmap.Width * 4;
                var pixels = new byte[rowBytes * bitmap.Height];
                for (var row = 0; row < bitmap.Height; row++)
                {
                    var sourceRow = data.Stride >= 0 ? row : bitmap.Height - 1 - row;
                    Marshal.Copy(data.Scan0 + sourceRow * data.Stride,
                        pixels, row * rowBytes, rowBytes);
                }
                publisher.PublishBgra(bitmap.Width, bitmap.Height, pixels);
                lastPublishedVideoMs = videoMs;
                if (!firstFramePublishedLogged)
                {
                    firstFramePublishedLogged = true;
                    log.Information(
                        "Flyleaf published first embedded frame: {Width}x{Height}, sequence={Sequence}, videoMs={VideoMs}",
                        bitmap.Width, bitmap.Height, publisher.Sequence, videoMs);
                }
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - lastFrameFailureLogUtc >= TimeSpan.FromSeconds(5))
            {
                lastFrameFailureLogUtc = now;
                log.Warning(ex,
                    "Flyleaf could not publish the embedded frame; audio may continue while the replay page remains black");
            }
        }
    }

    private string ResolveFfmpegPath()
    {
        if (!string.IsNullOrWhiteSpace(configuration.FlyleafFfmpegPath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                configuration.FlyleafFfmpegPath.Trim()));
        var assemblyDirectory = Path.GetDirectoryName(typeof(FlyleafPlaybackController).Assembly.Location)
                                ?? AppContext.BaseDirectory;
        return Path.Combine(assemblyDirectory, "FFmpeg");
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        try { dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send); } catch { }
        if (uiThread is { IsAlive: true }) uiThread.Join(TimeSpan.FromSeconds(2));
    }

    private static int ClampToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

    private sealed class PlaybackForm : Form
    {
        public PlaybackForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = System.Drawing.Color.Black;
            Width = 16;
            Height = 16;
        }

        protected override bool ShowWithoutActivation => true;
    }

}
