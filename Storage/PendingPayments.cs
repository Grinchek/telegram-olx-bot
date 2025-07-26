using Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Storage;

public static class PendingPayments
{
    private static readonly string FilePath = "data/payments.json";
    private static readonly object LockObj = new();
    private static readonly Dictionary<string, PaymentRequest> _pending = new();

    static PendingPayments()
    {
        Directory.CreateDirectory("data");
        if (!File.Exists(FilePath))
            File.WriteAllText(FilePath, "[]");
    }

    public static void Add(PaymentRequest request)
    {
        lock (LockObj)
        {
            _pending[request.Code] = request;
            SaveAll();
        }
    }

    public static bool TryGet(string code, out PaymentRequest? request)
    {
        lock (LockObj)
        {
            return _pending.TryGetValue(code, out request);
        }
    }

    public static void Remove(string code)
    {
        lock (LockObj)
        {
            _pending.Remove(code);
            SaveAll();
        }
    }

    public static List<PaymentRequest> GetAll()
    {
        lock (LockObj)
        {
            return _pending.Values.ToList();
        }
    }

    public static void Load()
    {
        lock (LockObj)
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<List<PaymentRequest>>(json);
            if (loaded != null)
            {
                _pending.Clear();
                foreach (var item in loaded)
                    _pending[item.Code] = item;
            }
        }
    }

    private static void SaveAll()
    {
        var list = _pending.Values.ToList();
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(FilePath, json);
    }
}
