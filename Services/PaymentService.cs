
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Data.Entities;
using Services.Interfaces;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Services;
using Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

public class PaymentService
{
    private readonly string _monoToken;
    private readonly IPendingPaymentsService _pendingPaymentsService;
    private readonly IConfirmedPaymentsService _confirmedPaymentsService;
    private readonly BotDbContext _context;

    public PaymentService(
        BotDbContext context,
        string monoToken,
        IPendingPaymentsService pendingPaymentsService,
        IConfirmedPaymentsService confirmedPaymentsService)
    {
        _context = context;
        _monoToken = monoToken;
        _pendingPaymentsService = pendingPaymentsService;
        _confirmedPaymentsService = confirmedPaymentsService;
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
        var pendingPayments = await _pendingPaymentsService.GetAllAsync();
        var newTransactions = await GetRecentTransactionsAsync();

        var matches = new List<PendingPayment>();

        //temp
        var usedTxIds = await _context.ConfirmedPayments
            .Select(x => x.TransactionId)
            .ToListAsync();

        var pickedTxIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


        foreach (var payment in pendingPayments)
        {
            var code = payment.Code.ToUpperInvariant();
            var regex = new Regex(@"\b" + Regex.Escape(code) + @"\b", RegexOptions.IgnoreCase);

            var matchTx = newTransactions.FirstOrDefault(t =>
                t.comment != null
                && regex.IsMatch(t.comment)
                && !usedTxIds.Contains(t.id)
                && !pickedTxIds.Contains(t.id));

            if (matchTx != null)
            {
                payment.TransactionId = matchTx.id;
                pickedTxIds.Add(matchTx.id);     // <- не дамо цю транзакцію використати вдруге в цьому ж проході
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
        var url = $"https://api.monobank.ua/personal/statement/0/{from}";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<MonobankTransaction>>(json) ?? new();
    }
}

public class MonobankTransaction
{
    public string id { get; set; }
    public string? comment { get; set; }
}
