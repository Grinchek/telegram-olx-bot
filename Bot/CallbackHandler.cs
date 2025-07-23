// /Bot/CallbackHandler.cs
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Storage;
using Services;
using Models;
using System.Threading.Tasks;
using System.Threading;
using System.Net;

namespace Bot
{
    public static class CallbackHandler
    {
        private static readonly string JarUrl = Environment.GetEnvironmentVariable("MONO_JAR_URL") ?? "";

        private static readonly long AdminChatId = long.Parse(Environment.GetEnvironmentVariable("ADMIN_CHAT_ID") ?? "0");


        public static async Task HandleCallbackAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var callback = update.CallbackQuery;
            if (callback == null) return;

            var chatId = callback.Message.Chat.Id;

            if (callback.Data == "confirm_publish")
            {
                if (!InMemoryRepository.PendingPosts.TryGetValue(chatId, out var post))
                {
                    await botClient.AnswerCallbackQueryAsync(callback.Id, "⛔ Немає оголошення для публікації.", cancellationToken: cancellationToken);
                    return;
                }

                bool isAdmin = chatId == AdminChatId;

                if (isAdmin)
                {
                    var messageId = await PostPublisher.PublishPostAsync(botClient, post, chatId, isAdmin: true, cancellationToken);
                    post.PublishedAt = DateTime.UtcNow;
                    post.ChannelMessageId = messageId;

                    var request = new PaymentRequest
                    {
                        ChatId = chatId,
                        Code = "FREE",
                        Post = post,
                        RequestedAt = DateTime.UtcNow,
                        TransactionId = null
                    };

                    ConfirmedPayments.Add(request);
                    InMemoryRepository.PendingPosts.TryRemove(chatId, out _);

                    await botClient.EditMessageReplyMarkupAsync(chatId, callback.Message.MessageId, null, cancellationToken);
                    await botClient.SendTextMessageAsync(chatId, "✅ Оголошення опубліковано безкоштовно (адмін).", cancellationToken: cancellationToken);
                }
                else
                {
                    var code = Program.PaymentService.GeneratePaymentCode(chatId, post);

                    await botClient.EditMessageReplyMarkupAsync(chatId, callback.Message.MessageId, null, cancellationToken);

                    await botClient.SendTextMessageAsync(chatId,
                        $"💳 Щоб опублікувати оголошення, сплати 15 грн на банку:\n" +
                        $"👉 <a href=\"{JarUrl}\">Натисни тут</a>\n\n" +
                        $"📝 У коментарі до платежу введи цей код: <code>{code}</code>\n\n" +
                        $"⏱ Після сплати бот автоматично перевірить оплату та опублікує оголошення впродовж 1–5 хвилин.",
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);

                }
            }
            else if (callback.Data == "cancel")
            {
                await botClient.EditMessageReplyMarkupAsync(chatId, callback.Message.MessageId, null, cancellationToken);
                await botClient.SendTextMessageAsync(chatId, "❌ Публікацію скасовано.", replyMarkup: KeyboardFactory.MainButtons(), cancellationToken: cancellationToken);
                InMemoryRepository.PendingPosts.TryRemove(chatId, out _);
            }
            else if (callback.Data.StartsWith("delete_post_"))
            {
                var encodedUrl = callback.Data.Replace("delete_post_", "");
                var url = WebUtility.UrlDecode(encodedUrl);

                var idFromCallback = callback.Data.Replace("delete_post_", "");
                var all = ConfirmedPayments.GetAll();
                var postToRemove = all.FirstOrDefault(p => p.Id == idFromCallback);


                if (postToRemove == null)
                {
                    await botClient.AnswerCallbackQueryAsync(callback.Id, "⚠️ Оголошення не знайдено.");
                    return;
                }

                bool isOwner = callback.From.Id == postToRemove.ChatId;
                bool isAdmin = callback.From.Id == AdminChatId;

                if (!isOwner && !isAdmin)
                {
                    await botClient.AnswerCallbackQueryAsync(callback.Id, "⛔ Ви можете видалити лише свої оголошення.");
                    return;
                }

                try
                {
                    await botClient.DeleteMessageAsync(
                        chatId: "@baraholka_market_ua",
                        messageId: postToRemove.Post!.ChannelMessageId!.Value,
                        cancellationToken: cancellationToken);

                    all.Remove(postToRemove);
                    ConfirmedPayments.Save();

                    await botClient.AnswerCallbackQueryAsync(callback.Id, "✅ Оголошення видалено.");
                }
                catch (Exception ex)
                {
                    await botClient.AnswerCallbackQueryAsync(callback.Id, $"❌ Помилка: {ex.Message}");
                }
            }
        }
    }
}
