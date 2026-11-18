using System;
using System.Reflection;
using FlyleafLib;
using WpfApplication = System.Windows.Application;

namespace PvpChronicleRecorder;

/// <summary>
/// Starts Flyleaf on the dispatcher that owns the Recorder playback surface.
/// Flyleaf creates a process-wide WPF Application when the host has none. After a Dalamud hot
/// reload that Application can remain registered even though its dispatcher has shut down, so a
/// later Engine.Start targets the dead dispatcher and throws TaskCanceledException.
/// </summary>
internal static class FlyleafEngineBootstrap
{
    internal const string PlaybackThreadName = "PVP Chronicle Flyleaf playback";
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    public static void Start(EngineConfig config)
    {
        if (Engine.IsLoaded) return;
        ResetDeadApplicationLeftByPreviousPluginLoad();
        Engine.Start(config);
    }

    /// <summary>
    /// WPF normally permits only one Application per process. A fully shut-down instance cannot
    /// dispatch work and cannot be reused, but its private registration survives in the host
    /// process. Clear only that provably dead registration so Flyleaf can create a replacement on
    /// the current dedicated STA thread. A live foreign Application is never changed.
    /// </summary>
    private static void ResetDeadApplicationLeftByPreviousPluginLoad()
    {
        var application = WpfApplication.Current;
        if (application == null ||
            (!application.Dispatcher.HasShutdownStarted &&
             !application.Dispatcher.HasShutdownFinished)) return;
        if (!string.Equals(application.Dispatcher.Thread.Name, PlaybackThreadName,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "检测到其他组件遗留的 WPF Application；为避免影响其他插件，Recorder 拒绝替换它");

        var type = typeof(WpfApplication);
        var globalLock = type.GetField("_globalLock", StaticPrivate)?.GetValue(null)
            ?? throw new MissingFieldException(type.FullName, "_globalLock");
        lock (globalLock)
        {
            if (!ReferenceEquals(WpfApplication.Current, application)) return;
            SetStaticField(type, "_appInstance", null);
            SetStaticField(type, "_appCreatedInThisAppDomain", false);
            SetStaticField(type, "_isShuttingDown", false);
        }
    }

    private static void SetStaticField(Type type, string name, object? value)
    {
        var field = type.GetField(name, StaticPrivate)
            ?? throw new MissingFieldException(type.FullName, name);
        field.SetValue(null, value);
    }
}
