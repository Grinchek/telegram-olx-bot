// /Services/PaymentPoller.cs
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Models;
using Storage;
using System;
using Bot;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public class PaymentPoller
{
    private readonly PaymentService _paymentService;
    private readonly ITelegramBotClient _bot;
    private readonly CancellationToken _token;

    private static readonly string ChannelUsername = Environment.GetEnvironmentVariable("CHANNEL_USERNAME") ?? "@baraholka_market_ua";
    private static readonly string BotUsername = Environment.GetEnvironmentVariable("BOT_USERNAME") ?? "@BARACHOLKA_UA_bot";


    public PaymentPoller(PaymentService paymentService, ITelegramBotClient bot, CancellationToken token)
    {
        _paymentService = paymentService;
        _bot = bot;
        _token = token;
    }

    private async Task TryPublishPostAsync(PaymentRequest payment)
    {
        if (!PostCounter.TryIncrement())
        {
            await _bot.SendTextMessageAsync(
                chatId: payment.ChatId,
                text: "⛔ Ліміт публікацій на сьогодні вичерпано. Спробуй завтра.",
                replyMarkup: KeyboardFactory.MainButtons(),
                cancellationToken: _token
            );
            return;
        }

        var messageId = await PostPublisher.PublishPostAsync(
            _bot,
            payment.Post!,
            payment.ChatId,
            isAdmin: false,
            cancellationToken: _token);

        payment.Post.ChannelMessageId = messageId;
        payment.Post.PublishedAt = DateTime.UtcNow;

        ConfirmedPayments.Add(payment);
        ConfirmedPayments.Save();

        await _bot.SendTextMessageAsync(
            chatId: payment.ChatId,
            text: "✅ Оголошення опубліковане в канал!",
            replyMarkup: KeyboardFactory.MainButtons(),
            cancellationToken: _token
        );
    }

    public async Task RunAsync()
    {
        // 🟡 КРОК 1: Опрацювати збережені, але не опубліковані пости
        var unpaidPosts = ConfirmedPayments.GetAll()
            .Where(p => p.Post != null && p.Post.ChannelMessageId == null)
            .ToList();

        foreach (var payment in unpaidPosts)
        {
            try
            {
                await TryPublishPostAsync(payment);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка при повторній публікації: {ex.Message}");
            }
        }


        // 🟢 КРОК 2: Перевірка нових оплат 
        while (!_token.IsCancellationRequested)
        {
            var confirmed = await _paymentService.CheckPaymentsAsync();

            foreach (var payment in confirmed)
            {
                if (payment.Post is null)
                {
                    Console.WriteLine($"⚠️ Оплата підтверджена, але немає оголошення для чату {payment.ChatId}");
                    continue;
                }

                if (payment.Post.ChannelMessageId != null)
                {
                    Console.WriteLine($"ℹ️ Оголошення вже опубліковано (MessageId: {payment.Post.ChannelMessageId})");
                    continue;
                }

                try
                {
                    await TryPublishPostAsync(payment);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Помилка при публікації: {ex.Message}");
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), _token);
        }
    }

}
