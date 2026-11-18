using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PvpChronicle.RecordingContract;

namespace PvpChronicleRecorder;

internal sealed class RecordingSessionStore
{
    private readonly object gate = new();
    private readonly string path;
    private Dictionary<string, RecordingAttachment> items = new(StringComparer.OrdinalIgnoreCase);

    public RecordingSessionStore(string path)
    {
        this.path = path;
        try
        {
            if (File.Exists(path))
            {
                var values = JsonSerializer.Deserialize<List<RecordingAttachment>>(
                    File.ReadAllText(path), RecordingContract.Json) ?? [];
                items = values.GroupBy(x => x.MatchUid, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { items.Clear(); }
    }

    public RecordingAttachment? Find(string uid)
    {
        lock (gate) return items.TryGetValue(uid, out var value) ? value : null;
    }

    public RecordingAttachment[] Snapshot()
    {
        lock (gate) return items.Values.ToArray();
    }

    public void Save(RecordingAttachment value)
    {
        lock (gate)
        {
            items[value.MatchUid] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items.Values.ToArray(), RecordingContract.Json));
            File.Move(temp, path, true);
        }
    }
}
