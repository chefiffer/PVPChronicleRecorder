using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace PvpChronicleRecorder;

internal static class PlaybackLauncher
{
    public static string Open(string videoPath, long videoMs, string configuredPlayer)
    {
        if (!File.Exists(videoPath)) return "video_missing";
        var player = ResolvePlayer(configuredPlayer);
        if (player == null)
        {
            Process.Start(new ProcessStartInfo(videoPath) { UseShellExecute = true });
            return "opened_without_seek";
        }
        var argument = BuildArguments(player, videoPath, videoMs);
        Process.Start(new ProcessStartInfo(player, argument) { UseShellExecute = false });
        return "opened";
    }

    internal static string BuildArguments(string player, string videoPath, long videoMs)
    {
        var seconds = Math.Max(0, videoMs) / 1000d;
        var name = Path.GetFileNameWithoutExtension(player);
        if (name.Contains("vlc", StringComparison.OrdinalIgnoreCase))
            return $"--start-time={seconds.ToString("0.###", CultureInfo.InvariantCulture)} \"{videoPath}\"";
        if (name.Contains("potplayer", StringComparison.OrdinalIgnoreCase))
        {
            var position = TimeSpan.FromMilliseconds(Math.Max(0, videoMs));
            var seek = $"{(int)position.TotalHours:00}:{position.Minutes:00}:{position.Seconds:00}.{position.Milliseconds:000}";
            // PotPlayer ships this syntax in CmdLine.txt. /current keeps one
            // playback window and /seek moves it to the replay cursor.
            return $"/current /seek={seek} \"{videoPath}\"";
        }
        return $"--start={seconds.ToString("0.###", CultureInfo.InvariantCulture)} \"{videoPath}\"";
    }

    private static string? ResolvePlayer(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "mpv", "mpv.exe")
        };
        return Array.Find(candidates, File.Exists);
    }
}
