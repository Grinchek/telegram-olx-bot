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

    public async Task RunAsync()
    {
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

                // Перевірка, чи вже опубліковано
                if (payment.Post.ChannelMessageId != null)
                {
                    Console.WriteLine($"ℹ️ Оголошення вже опубліковано (MessageId: {payment.Post.ChannelMessageId})");
                    continue;
                }
                try
                {
                    var messageId = await PostPublisher.PublishPostAsync(
                        _bot,
                        payment.Post,
                        payment.ChatId,
                        isAdmin: false,
                        cancellationToken: _token);

                    payment.Post.ChannelMessageId = messageId;
                    payment.Post.PublishedAt = DateTime.UtcNow;

                    ConfirmedPayments.Add(payment);
                    ConfirmedPayments.Save(); // на всяк випадок

                    await _bot.SendTextMessageAsync(
                        chatId: payment.ChatId,
                        text: "✅ Оголошення опубліковане в канал!",
                        replyMarkup: KeyboardFactory.MainButtons(),
                        cancellationToken: _token
                    );

                    Console.WriteLine($"📢 Опубліковано оголошення для чату {payment.ChatId} (msgId: {messageId})");
                }

                //try
                //{
                //    var caption = CaptionBuilder.Build(payment.Post, true, BotUsername);

                //    var sentMessage = await _bot.SendPhotoAsync(
                //        chatId: ChannelUsername,
                //        photo: InputFile.FromUri(payment.Post.ImageUrl ?? "https://via.placeholder.com/300"),
                //        caption: caption,
                //        parseMode: ParseMode.Html,
                //        cancellationToken: _token
                //    );

                //    payment.Post.ChannelMessageId = sentMessage.MessageId;
                //    payment.Post.PublishedAt = DateTime.UtcNow;

                //    ConfirmedPayments.Add(payment);

                //    await _bot.SendTextMessageAsync(
                //        chatId: payment.ChatId,
                //        text: "✅ Оголошення опубліковане в канал!",
                //        replyMarkup: KeyboardFactory.MainButtons(),
                //        cancellationToken: _token
                //    );

                //    Console.WriteLine($"📢 Опубліковано оголошення для чату {payment.ChatId} (msgId: {sentMessage.MessageId})");
                //}
                catch (Exception ex)
                {
                    await _bot.SendTextMessageAsync(
                        chatId: payment.ChatId,
                        text: $"⚠️ Помилка при публікації: {ex.Message}",
                        cancellationToken: _token
                    );
                    Console.WriteLine($"❌ Помилка публікації для {payment.ChatId}: {ex.Message}");
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), _token);
        }
    }
}
