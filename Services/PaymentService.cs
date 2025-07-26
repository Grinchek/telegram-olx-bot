//Services/PaymentService.cs
using Models;
using Storage;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Services;

public class PaymentService
{
    private readonly string _token;
    private readonly HttpClient _http;

    public PaymentService(string token)
    {
        _token = token;
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.monobank.ua/")
        };
        _http.DefaultRequestHeaders.Add("X-Token", _token);
    }

    public string GeneratePaymentCode(long chatId, PostData? post = null)
    {
        var code = new Random().Next(10000, 99999).ToString();

        PendingPayments.Add(new PaymentRequest
        {
            ChatId = chatId,
            Code = code,
            Post = post
        });

        return code;
    }

    public async Task<List<PaymentRequest>> CheckPaymentsAsync()
    {
        var transactions = await GetRecentTransactionsAsync();
        var confirmed = new List<PaymentRequest>();

        foreach (var transaction in transactions)
        {
            if (!IsRelevant(transaction)) continue;

            var code = ExtractCode(transaction.comment!);
            if (code is null) continue;

            if (PendingPayments.TryGet(code, out var request))
            {
                request.TransactionId = transaction.id;
                confirmed.Add(request);
                PendingPayments.Remove(code);
                Console.WriteLine($"✅ Підтверджено оплату по коду: {code}");
            }
        }

        Console.WriteLine($"🔎 Перевірено {transactions.Count} транзакцій. Підтверджено: {confirmed.Count}");
        await SaveConfirmedPaymentsAsync(confirmed);
        return confirmed;
    }

    private async Task<List<MonobankTransaction>> GetRecentTransactionsAsync()
    {
        var from = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds();
        var to = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeSeconds();
        var accountId = Environment.GetEnvironmentVariable("MONOBANK_ACCOUNT_ID") ?? "";

        var response = await _http.GetAsync($"personal/statement/{accountId}/{from}/{to}");

        if (!response.IsSuccessStatusCode)
            return [];

        var jsonContent = await response.Content.ReadAsStringAsync();
        var transactions = JsonSerializer.Deserialize<List<MonobankTransaction>>(jsonContent)!;

        foreach (var tx in transactions)
        {
            Console.WriteLine($"TX: {tx.amount / 100.0} грн, currency: {tx.currencyCode}, comment: '{tx.comment}', time: {tx.time}, id: {tx.id}");
        }

        return transactions;
    }

    private static bool IsRelevant(MonobankTransaction tx) =>
        tx.amount >= 1500 && tx.currencyCode == 980 && !string.IsNullOrWhiteSpace(tx.comment);

    private static string? ExtractCode(string comment)
    {
        var match = Regex.Match(comment, @"(?<!\d)\d{5,6}(?!\d)");
        return match.Success ? match.Value : null;
    }

    private static async Task SaveConfirmedPaymentsAsync(List<PaymentRequest> confirmed)
    {
        const string filePath = "data/payments.json";
        List<PaymentRequest> history = [];

        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var loaded = JsonSerializer.Deserialize<List<PaymentRequest>>(json);
                if (loaded is not null)
                    history = loaded;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Не вдалося прочитати файл: {ex.Message}");
            }
        }

        history.AddRange(confirmed);

        try
        {
            var jsonOutput = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, jsonOutput);
            Console.WriteLine("💾 Збережено у payments.json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Не вдалося записати у файл: {ex.Message}");
        }
    }

    private class MonobankTransaction
    {
        public string id { get; set; } = default!;
        public long time { get; set; }
        public int amount { get; set; }
        public int currencyCode { get; set; }
        public string? comment { get; set; }
    }
}

