using Models;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Storage;

public static class ConfirmedPayments
{
    private static readonly string FilePath = Path.Combine("data", "payments.json");

    private static readonly List<PaymentRequest> Confirmed = new();

    public static void Add(PaymentRequest request)
    {
        Confirmed.Add(request);
        Save();
    }

    public static void Save()
    {
        var json = JsonSerializer.Serialize(Confirmed, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, json);
    }

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        var json = File.ReadAllText(FilePath);
        var loaded = JsonSerializer.Deserialize<List<PaymentRequest>>(json);
        if (loaded != null)
            Confirmed.AddRange(loaded);
    }
    public static void RemoveDuplicatesByChannelMessageId()
    {
        var seen = new HashSet<int>();
        Confirmed.RemoveAll(p =>
        {
            if (p.Post?.ChannelMessageId == null) return false;
            return !seen.Add(p.Post.ChannelMessageId.Value);
        });

        Save();
    }


    public static List<PaymentRequest> GetAll() => Confirmed;
}
