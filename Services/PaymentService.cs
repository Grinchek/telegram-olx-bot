using Models;
using Storage;
using System.Collections.Generic;
using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

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

    // Генерує унікальний 5-значний код
    public string GeneratePaymentCode(long chatId, PostData? post = null)
    {
        var code = new Random().Next(10000, 99999).ToString();

        var request = new PaymentRequest
        {
            ChatId = chatId,
            Code = code,
            Post = post
        };

        PendingPayments.Add(request);
        return code;
    }

    public async Task<List<PaymentRequest>> CheckPaymentsAsync()
    {

        var from = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds();
        var to = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeSeconds();

        var accountId = Environment.GetEnvironmentVariable("MONOBANK_ACCOUNT_ID") ?? "";
        // ID банки
        var response = await _http.GetAsync($"personal/statement/{accountId}/{from}/{to}");

        if (!response.IsSuccessStatusCode)
            return new List<PaymentRequest>(); // або логування

        var json = await response.Content.ReadAsStringAsync();
        var transactions = JsonSerializer.Deserialize<List<MonobankTransaction>>(json)!;
        foreach (var tx in transactions)
        {
            Console.WriteLine($"TX: {tx.amount / 100.0} грн, currency: {tx.currencyCode}, comment: '{tx.comment}', time: {tx.time}, id: {tx.id}");
        }

        var confirmed = new List<PaymentRequest>();

        foreach (var tx in transactions)
        {
            if (tx.amount < 1000 || tx.currencyCode != 980 || string.IsNullOrWhiteSpace(tx.comment))
                continue;

            var codeMatch = System.Text.RegularExpressions.Regex.Match(tx.comment ?? "", @"(?<!\d)\d{5,6}(?!\d)");


            if (codeMatch.Success)
            {
                var code = codeMatch.Value;

                if (PendingPayments.TryGet(code, out var request))
                {
                    request.TransactionId = tx.id;
                    confirmed.Add(request);
                    PendingPayments.Remove(code);
                    Console.WriteLine($"✅ Підтверджено оплату по коду: {code}");
                }
            }
        }

        Console.WriteLine($"🔎 Перевірено {transactions.Count} транзакцій. Підтверджено: {confirmed.Count}");

        // === НОВА ЛОГІКА ===

        const string filePath = "data/payments.json";

        // 1. Прочитай історію з файлу, якщо є
        List<PaymentRequest> history = [];

        if (File.Exists(filePath))
        {
            try
            {
                var jsonHistory = await File.ReadAllTextAsync(filePath);
                var loaded = JsonSerializer.Deserialize<List<PaymentRequest>>(jsonHistory);
                if (loaded is not null)
                    history = loaded;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Не вдалося прочитати payments.json: {ex.Message}");
            }
        }

        // 2. Додай нові підтвердження до історії
        history.AddRange(confirmed);

        // 3. Запиши в файл оновлену історію
        try
        {
            var jsonOutput = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, jsonOutput);
            Console.WriteLine("💾 Збережено у payments.json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Не вдалося записати у payments.json: {ex.Message}");
        }

        return confirmed;

    }

    // Внутрішня модель Monobank
    private class MonobankTransaction
    {
        public string id { get; set; } = default!;
        public long time { get; set; }
        public int amount { get; set; }
        public int currencyCode { get; set; }
        public string? comment { get; set; }
    }

    // Тимчасово не використовується
    //public async Task ListAccountsAsync()
    //{
    //    var response = await _http.GetAsync("personal/client-info");
    //    var json = await response.Content.ReadAsStringAsync();
    //    Console.WriteLine(json);
    //}
}
