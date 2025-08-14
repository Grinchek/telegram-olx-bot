using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Services.Interfaces;
using Data.Entities;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using Services;

namespace Bot;

public class CallbackHandler
{
    private readonly IConfirmedPaymentsService _confirmedPaymentsService;
    private readonly IPendingPaymentsService _pendingPaymentsService;
    private readonly IPostDraftService _postDraftSeevice;
    private readonly string _jarUrl;
    private readonly long _adminChatId;
    private readonly PostPublisher _postPublisher;



    public CallbackHandler(
        IConfirmedPaymentsService confirmedPaymentsService,
        IPendingPaymentsService pendingPaymentsService,
        IPostDraftService postDraftSeevice,
        PostPublisher postPublisher)
    {
        _postPublisher = postPublisher;
        _confirmedPaymentsService = confirmedPaymentsService;
        _pendingPaymentsService = pendingPaymentsService;
        _postDraftSeevice = postDraftSeevice;
        _jarUrl = Environment.GetEnvironmentVariable("MONO_JAR_URL") ?? "";
        _adminChatId = long.Parse(Environment.GetEnvironmentVariable("ADMIN_CHAT_ID") ?? "0");
    }

    public async Task HandleCallbackAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var callback = update.CallbackQuery;
        if (callback == null) return;

        var chatId = callback.Message.Chat.Id;

        if (callback.Data == "confirm_publish")
        {
            var pending = await _pendingPaymentsService.GetLastByChatIdAsync(chatId);

            if (pending == null || pending.Post == null)
            {
                await botClient.AnswerCallbackQueryAsync(callback.Id, "⛔ Немає оголошення для публікації.", cancellationToken: cancellationToken);
                return;
            }

            var post = pending.Post!;
            bool isAdmin = chatId == _adminChatId;

            if (isAdmin)
            {
                var messageId = await _postPublisher.PublishPostAsync(post, chatId, true, cancellationToken);
                post.PublishedAt = DateTime.UtcNow;
                post.ChannelMessageId = messageId;

                var request = new ConfirmedPayment
                {
                    ChatId = chatId,
                    Code = "FREE",
                    Post = post,
                    RequestedAt = DateTime.UtcNow,
                    TransactionId = null
                };

                await _confirmedPaymentsService.AddAsync(request);
                await _pendingPaymentsService.RemoveAsync(pending);

                await botClient.EditMessageReplyMarkupAsync(chatId, callback.Message.MessageId, null, cancellationToken);
                await botClient.SendTextMessageAsync(chatId, "✅ Оголошення опубліковано безкоштовно (адмін).", cancellationToken: cancellationToken);
            }
            else
            {
                var code = await Program.PaymentService.GeneratePaymentCode(chatId, post);

                await botClient.EditMessageReplyMarkupAsync(chatId, callback.Message.MessageId, null, cancellationToken);

                await botClient.SendTextMessageAsync(chatId,
                    $"💳 Щоб опублікувати оголошення, сплати 15 грн на банку:\n" +
                    $"👉 <a href=\"{_jarUrl}\">Натисни тут</a>\n\n" +
                    $"📝 У коментарі до платежу введи цей код: <code>{code}</code>\n\n" +
                    $"⏱ Після сплати бот автоматично перевірить оплату та опублікує оголошення впродовж 1–5 хвилин.",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
        }
        else if (callback.Data == "cancel")
        {
            var pending = await _pendingPaymentsService.GetLastByChatIdAsync(chatId);
            if (pending != null)
                await _pendingPaymentsService.RemoveAsync(pending);
                await _postDraftSeevice.RemoveByChatIdAsync(pending.ChatId);

            await botClient.EditMessageReplyMarkupAsync(chatId, callback.Message.MessageId, null, cancellationToken);
            await botClient.SendTextMessageAsync(chatId, "❌ Публікацію скасовано.", replyMarkup: KeyboardFactory.MainButtons(), cancellationToken: cancellationToken);
        }
        else if (callback.Data.StartsWith("delete_post_msg_"))
        {
            var msgIdRaw = callback.Data.Replace("delete_post_msg_", "");

            if (!int.TryParse(msgIdRaw, out int msgId))
            {
                await botClient.AnswerCallbackQueryAsync(callback.Id, "⚠️ Некоректний формат ID.");
                return;
            }

            // Отримуємо конкретний ConfirmedPayment з AsNoTracking, щоб уникнути проблем EF
            var postToRemove = await _confirmedPaymentsService.GetByChannelMessageIdAsync(msgId);

            if (postToRemove == null)
            {
                await botClient.AnswerCallbackQueryAsync(callback.Id, "⚠️ Оголошення не знайдено.");
                return;
            }

            bool isOwner = callback.From.Id == postToRemove.ChatId;
            bool isAdmin = callback.From.Id == _adminChatId;

            if (!isOwner && !isAdmin)
            {
                await botClient.AnswerCallbackQueryAsync(callback.Id, "⛔ Ви можете видалити лише свої оголошення.");
                return;
            }

            try
            {
                // Видаляємо повідомлення з каналу
                await botClient.DeleteMessageAsync(
                    chatId: "@baraholka_market_ua",
                    messageId: msgId,
                    cancellationToken: cancellationToken);

                // Видаляємо з таблиці confirmed_payments
                await _confirmedPaymentsService.RemoveAsync(postToRemove);
                // Видаляємо чернетку з таблиці post
                var affected = await _postDraftSeevice.RemoveByChannelMessageIdAsync(msgId);
                if (affected == 0)
                {
                    await _postDraftSeevice.RemoveByPostIdAsync(postToRemove.PostId);
                }

                await botClient.AnswerCallbackQueryAsync(callback.Id, "✅ Оголошення видалено.");

            }

            catch (Exception ex)
            {
                // Обрізаємо повідомлення помилки до 180 символів, щоб уникнути MESSAGE_TOO_LONG
                var shortError = ex.Message.Length > 180 ? ex.Message[..180] + "..." : ex.Message;
                await botClient.AnswerCallbackQueryAsync(callback.Id, $"❌ Помилка: {shortError}");
            }

        }



    }
}
