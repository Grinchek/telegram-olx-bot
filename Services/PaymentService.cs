
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using Data.Entities;
using Services.Interfaces;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Services;
using Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

public class PaymentService
{
    private readonly string _monoToken;
    private readonly string _monoAccountId; // <-- ДОДАЛИ
    private readonly IPendingPaymentsService _pendingPaymentsService;
    private readonly IConfirmedPaymentsService _confirmedPaymentsService;
    private readonly BotDbContext _context;

    public PaymentService(
        BotDbContext context,
        IConfiguration cfg, // <-- ЗАМІНИЛИ string monoToken на IConfiguration
        IPendingPaymentsService pendingPaymentsService,
        IConfirmedPaymentsService confirmedPaymentsService)
    {
        _context = context;
        _pendingPaymentsService = pendingPaymentsService;
        _confirmedPaymentsService = confirmedPaymentsService;

        _monoToken = cfg["MONOBANK_TOKEN"]
            ?? throw new InvalidOperationException("MONOBANK_TOKEN is not set");
        _monoAccountId = cfg["MONOBANK_ACCOUNT_ID"] ?? "0"; 
    }

    // Генерація унікального коду
    public async Task<string> GeneratePaymentCode(long chatId, PostData post)
    {
        var code = Guid.NewGuid().ToString("N")[..6].ToUpper();
        var pending = new PendingPayment
        {
            ChatId = chatId,
            Code = code,
            RequestedAt = DateTime.UtcNow,
            Post = post
        };
        await _pendingPaymentsService.AddAsync(pending);
        return code;
    }

    public async Task ConfirmPaymentAsync(PendingPayment pending)
    {
        // Завантаж пост із поточного контексту
        var post = await _context.Posts.FirstAsync(p => p.Id == pending.PostId);

        var confirmed = new ConfirmedPayment
        {
            ChatId = pending.ChatId,
            Code = pending.Code,
            RequestedAt = pending.RequestedAt,
            TransactionId = pending.TransactionId,
            PostId = post.Id
        };

        await _confirmedPaymentsService.AddAsync(confirmed);
        await _pendingPaymentsService.RemoveAllByChatIdAsync(pending.ChatId);
    }


    // Отримання нових оплат, які відповідають очікуваним
  
    public async Task<List<PendingPayment>> GetNewPaymentsAsync()
    {
        Console.WriteLine("Початок пошуку нових оплат...");
        var pendingPayments = await _pendingPaymentsService.GetAllAsync();
        var newTransactions = await GetRecentTransactionsAsync();
        
        var matches = new List<PendingPayment>();

        var usedTxIds = await _context.ConfirmedPayments
            .Select(x => x.TransactionId)
            .ToListAsync();

        var pickedTxIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const int CURRENCY_UAH = 980;
        const int MIN_AMOUNT_KOP = 1500; // 15.00 грн

        

        foreach (var payment in pendingPayments)
        {
            if (string.IsNullOrWhiteSpace(payment.Code))
                continue;

            var code = payment.Code.Trim();


            static bool CommentContainsCode(string? comment, string code)
            {
                if (string.IsNullOrWhiteSpace(comment) || string.IsNullOrWhiteSpace(code))
                    return false;

                var c = comment.Normalize(NormalizationForm.FormKC);
                var k = code.Normalize(NormalizationForm.FormKC);

                return c.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            ///////////////////////////////
            foreach (var t in newTransactions)
            {
                var hasCode = CommentContainsCode(t.comment, code);
                var rightCur = t.currencyCode == CURRENCY_UAH;
                var enough = t.amount >= MIN_AMOUNT_KOP && t.amount > 0;
                if (hasCode)
                    Console.WriteLine($"tx {t.id}: hasCode={hasCode}, curr={t.currencyCode}, amount={t.amount}");
            }
            var matchTx = newTransactions.FirstOrDefault(t =>
                CommentContainsCode(t.comment, code)    
                && t.currencyCode == CURRENCY_UAH   
                && t.amount >= MIN_AMOUNT_KOP
                && t.amount > 0
                && !usedTxIds.Contains(t.id)
                && !pickedTxIds.Contains(t.id)
            );

            if (matchTx != null)
            {
                payment.TransactionId = matchTx.id;
                pickedTxIds.Add(matchTx.id);
                matches.Add(payment);
            }
        }
      
        return matches;
    }


    // Отримати останні 100 транзакцій із Монобанку
    private async Task<List<MonobankTransaction>> GetRecentTransactionsAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-Token", _monoToken);

        var from = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();

        var url = $"https://api.monobank.ua/personal/statement/{_monoAccountId}/{from}";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        return JsonSerializer.Deserialize<List<MonobankTransaction>>(json, options) ?? new();
    }
}

public class MonobankTransaction
{
    public string id { get; set; }
    public string? comment { get; set; }
    public long amount { get; set; }     
    public int currencyCode { get; set; }
}

