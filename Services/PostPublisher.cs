using System.Threading.Tasks;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Models;
using Storage;
using System;
using Bot;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace Services;

public static class PostPublisher
{
    private static readonly string BotUsername = Environment.GetEnvironmentVariable("BOT_USERNAME") ?? "@BARACHOLKA_UA_bot";
    private static readonly string ChannelUsername = Environment.GetEnvironmentVariable("CHANNEL_USERNAME") ?? "@baraholka_market_ua";
    private static readonly long AdminChatId = long.Parse(Environment.GetEnvironmentVariable("ADMIN_CHAT_ID") ?? "0");


    public static async Task<int> PublishPostAsync(ITelegramBotClient botClient, PostData post, long chatId, bool isAdmin, CancellationToken cancellationToken)
    {

        var caption = CaptionBuilder.Build(post, isAdmin, BotUsername);
        var imageUrl = post.ImageUrl ?? "https://via.placeholder.com/300";

        // Створюємо унікальний код для видалення
        var deleteId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Створюємо payment request (і зберігаємо deleteId)
        var request = new PaymentRequest
        {
            ChatId = chatId,
            Code = isAdmin ? "FREE" : Program.PaymentService.GeneratePaymentCode(chatId, post),
            Post = post,
            RequestedAt = DateTime.UtcNow,
            TransactionId = null,
            Id = deleteId // нове поле
        };

        // Додаємо до ConfirmedPayments
        List < PaymentRequest > Confirmed= ConfirmedPayments.GetAll();
        foreach (var item in Confirmed)
        {
           if(item.Post.ChannelMessageId == request.Post.ChannelMessageId)
            {
                
            }
        }
        ConfirmedPayments.Add(request);


        // Формуємо кнопку
        var markup = new InlineKeyboardMarkup(new[]
        {
        InlineKeyboardButton.WithCallbackData("🗑 Видалити", $"delete_post_{request.Id}")
    });

        var message = await botClient.SendPhotoAsync(
            chatId: ChannelUsername,
            photo: InputFile.FromUri(imageUrl),
            caption: caption,
            parseMode: ParseMode.Html,
            replyMarkup: markup,
            cancellationToken: cancellationToken);

        post.PublishedAt = DateTime.UtcNow;
        post.ChannelMessageId = message.MessageId;

        // Зберігаємо оновлене повідомлення в ConfirmedPayments (опціонально)
        ConfirmedPayments.RemoveDuplicatesByChannelMessageId();
        ConfirmedPayments.Save();

        return message.MessageId;
    }

}