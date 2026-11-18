using System;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Configuration;

namespace PvpChronicleRecorder;

internal sealed class Configuration : IPluginConfiguration
{
    private const string Prefix = "dpapi:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PvpChronicleRecorder.ObsPassword.v1");

    public int Version { get; set; } = 1;
    public string Language { get; set; } = "auto";
    public bool AutoRecord { get; set; } = true;
    public string ObsWebSocketUrl { get; set; } = "ws://127.0.0.1:4455";
    public string ProtectedObsPassword { get; set; } = string.Empty;
    public string VideoPlayerPath { get; set; } = string.Empty;
    public string FlyleafFfmpegPath { get; set; } = string.Empty;
    public long FirstFrameCalibrationMs { get; set; }

    public string GetObsPassword()
    {
        if (!ProtectedObsPassword.StartsWith(Prefix, StringComparison.Ordinal)) return string.Empty;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(ProtectedObsPassword[Prefix.Length..]), Entropy,
                DataProtectionScope.CurrentUser));
        }
        catch { return string.Empty; }
    }

    public void SetObsPassword(string? value)
    {
        var text = value ?? string.Empty;
        ProtectedObsPassword = text.Length == 0 ? string.Empty : Prefix + Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy, DataProtectionScope.CurrentUser));
    }
}
