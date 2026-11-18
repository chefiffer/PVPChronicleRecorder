using System;
using System.IO;
using System.Text.Json;
using PvpChronicle.RecordingContract;

namespace PvpChronicleRecorder;

internal sealed record ActiveRecordingState(
    string Schema,
    MatchRecordingEvent Match,
    DateTimeOffset RecordingStartedAtUtc);

/// <summary>
/// Persists the one recording that OBS is currently writing. Dalamud may hot-reload this plugin
/// while OBS intentionally keeps recording; without this journal the later match-ended event
/// cannot be matched to its start and OBS would never receive StopRecord.
/// </summary>
internal sealed class ActiveRecordingStore
{
    private readonly string path;

    public ActiveRecordingStore(string path) => this.path = path;

    public ActiveRecordingState? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var value = JsonSerializer.Deserialize<ActiveRecordingState>(
                File.ReadAllText(path), RecordingContract.Json);
            return value?.Schema == RecordingContract.Schema &&
                   value.Match.Schema == RecordingContract.Schema
                ? value : null;
        }
        catch
        {
            // A partial journal must not stop the plugin from loading. The next confirmed start
            // atomically replaces it, while the original remains available for diagnosis.
            return null;
        }
    }

    public void Save(MatchRecordingEvent match, DateTimeOffset recordingStartedAtUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(
            new ActiveRecordingState(RecordingContract.Schema, match, recordingStartedAtUtc),
            RecordingContract.Json));
        File.Move(temp, path, true);
    }

    public void Clear()
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
