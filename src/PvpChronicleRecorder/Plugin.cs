using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace PvpChronicleRecorder;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration configuration;
    private readonly WindowSystem windows = new("PvpChronicleRecorder");
    private readonly RecordingController controller;
    private readonly RecorderWindow window;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        RecorderLocalization.Configure(configuration.Language);
        var sessions = new RecordingSessionStore(Path.Combine(
            PluginInterface.GetPluginConfigDirectory(), "recording-sessions.json"));
        var activeRecording = new ActiveRecordingStore(Path.Combine(
            PluginInterface.GetPluginConfigDirectory(), "active-recording.json"));
        controller = new RecordingController(PluginInterface, configuration, sessions,
            activeRecording, Log);
        var runtimeInstaller = new FlyleafRuntimeInstaller(
            PluginInterface.GetPluginConfigDirectory(), configuration, Save, Log);
        window = new RecorderWindow(configuration, controller, runtimeInstaller, Save);
        windows.AddWindow(window);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += Toggle;
        Commands.AddHandler("/pvprec", new CommandInfo((_, _) => Toggle())
        {
            HelpMessage = RecorderLocalization.T("commandHelp")
        });
    }

    private void Save() => PluginInterface.SavePluginConfig(configuration);
    private void Toggle() => window.Toggle();

    public void Dispose()
    {
        Commands.RemoveHandler("/pvprec");
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
        controller.Dispose();
        windows.RemoveAllWindows();
    }
}
