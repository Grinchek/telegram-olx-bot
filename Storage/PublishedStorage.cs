// /Storage/PublishedStorage.cs
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Storage;

public static class PublishedStorage
{
    private static readonly string FilePath = "data/published.json";
    private static readonly HashSet<string> PublishedIds = Load();

    private static HashSet<string> Load()
    {
        if (!File.Exists(FilePath))
        {
            Directory.CreateDirectory("data");
            File.WriteAllText(FilePath, "[]");
        }

        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<HashSet<string>>(json) ?? new();
    }

    public static bool IsPublished(string txId) => PublishedIds.Contains(txId);

    public static void MarkAsPublished(string txId)
    {
        if (PublishedIds.Add(txId))
            File.WriteAllText(FilePath, JsonSerializer.Serialize(PublishedIds));
    }
}
