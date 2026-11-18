using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PvpChronicleRecorder;

internal sealed class RecorderWindow : Window
{
    private readonly Configuration configuration;
    private readonly RecordingController controller;
    private readonly FlyleafRuntimeInstaller runtimeInstaller;
    private readonly Action save;
    private string password;
    private string testResult = string.Empty;
    private bool testing;

    public RecorderWindow(Configuration configuration, RecordingController controller,
        FlyleafRuntimeInstaller runtimeInstaller, Action save)
        : base("PVP Chronicle Recorder###PvpChronicleRecorder")
    {
        this.configuration = configuration;
        this.controller = controller;
        this.runtimeInstaller = runtimeInstaller;
        this.save = save;
        password = configuration.GetObsPassword();
        Size = new Vector2(680, 650);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        DrawLanguageSelector();
        ImGui.TextWrapped(T("intro"));
        ImGui.Separator();
        var auto = configuration.AutoRecord;
        if (ImGui.Checkbox(T("autoRecord") + "##auto-record", ref auto))
        {
            configuration.AutoRecord = auto;
            save();
        }

        DrawFieldHeader(T("obsUrl"), T("obsUrlHelp"));
        var url = configuration.ObsWebSocketUrl;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##obs-url", ref url, 512)) configuration.ObsWebSocketUrl = url;

        DrawFieldHeader(T("obsPassword"), T("obsPasswordHelp"));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##obs-password", ref password, 512, ImGuiInputTextFlags.Password);

        DrawFieldHeader(T("ffmpegPath"), T("ffmpegPathHelp"));
        var ffmpeg = configuration.FlyleafFfmpegPath;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##ffmpeg-path", ref ffmpeg, 1024))
            configuration.FlyleafFfmpegPath = ffmpeg;
        if (!runtimeInstaller.Installing && ImGui.Button(T("installRuntime")))
            _ = runtimeInstaller.InstallAsync();
        if (runtimeInstaller.Installing)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(T("installing"));
        }
        if (!string.IsNullOrWhiteSpace(runtimeInstaller.Status))
            ImGui.TextWrapped(runtimeInstaller.Status);
        if (runtimeInstaller.Installing || runtimeInstaller.DownloadProgress > 0 ||
            runtimeInstaller.VerifyProgress > 0 || runtimeInstaller.ExtractProgress > 0)
        {
            DrawProgress(T("download"), runtimeInstaller.DownloadProgress);
            DrawProgress(T("verify"), runtimeInstaller.VerifyProgress);
            DrawProgress(T("extract"), runtimeInstaller.ExtractProgress);
        }

        DrawFieldHeader(T("calibration"), T("calibrationHelp"));
        var calibration = (int)Math.Clamp(configuration.FirstFrameCalibrationMs, -5000, 5000);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt("##first-frame-calibration", ref calibration, -5000, 5000))
            configuration.FirstFrameCalibrationMs = calibration;
        if (ImGui.Button(T("save")))
        {
            configuration.SetObsPassword(password);
            save();
        }
        ImGui.SameLine();
        if (!testing && ImGui.Button(T("testObs")))
        {
            configuration.SetObsPassword(password);
            save();
            testing = true;
            _ = Task.Run(async () =>
            {
                testResult = await controller.TestObsAsync();
                testing = false;
            });
        }
        ImGui.Separator();
        ImGui.TextWrapped(T("status") + ": " + controller.Status);
        if (!string.IsNullOrWhiteSpace(testResult)) ImGui.TextWrapped(testResult);
    }

    private void DrawLanguageSelector()
    {
        var languageIndex = Array.IndexOf(RecorderLocalization.Codes, configuration.Language);
        if (languageIndex < 0) languageIndex = 0;
        ImGui.TextUnformatted(T("language"));
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.Combo("##recorder-language", ref languageIndex,
                string.Join('\0', RecorderLocalization.Names) + "\0")) return;
        configuration.Language = RecorderLocalization.Codes[languageIndex];
        RecorderLocalization.Configure(configuration.Language);
        testResult = string.Empty;
        save();
    }

    private static void DrawFieldHeader(string title, string help)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(title);
        ImGui.TextDisabled(help);
    }

    private static string T(string key) => RecorderLocalization.T(key);

    private static void DrawProgress(string label, double value)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine(100);
        ImGui.ProgressBar((float)Math.Clamp(value, 0, 1), new Vector2(-1, 0),
            $"{Math.Clamp(value, 0, 1) * 100:0}%");
    }
}
