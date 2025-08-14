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

        var chatId = callback.Message?.Chat.Id ?? 0;
        var messageId = callback.Message?.MessageId;
        var inlineMessageId = callback.InlineMessageId;

        if (callback.Data == "confirm_publish")
        {
            var pending = await _pendingPaymentsService.GetLastByChatIdAsync(chatId);
            if (pending == null || pending.Post == null)
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: "⛔ Немає оголошення для публікації.",
                    cancellationToken: cancellationToken);
                return;
            }

            var post = pending.Post!;
            bool isAdmin = chatId == _adminChatId;

            if (isAdmin)
            {
                var publishedMsgId = await _postPublisher.PublishPostAsync(post, chatId, true, cancellationToken);
                post.PublishedAt = DateTime.UtcNow;
                post.ChannelMessageId = publishedMsgId;

                var confirmed = new ConfirmedPayment
                {
                    ChatId = chatId,
                    Code = "FREE",
                    Post = post,
                    RequestedAt = DateTime.UtcNow,
                    TransactionId = null
                };

                await _confirmedPaymentsService.AddAsync(confirmed);
                await _pendingPaymentsService.RemoveAsync(pending);

                if (messageId.HasValue)
                {
                    await botClient.EditMessageReplyMarkupAsync(
                        chatId: chatId,
                        messageId: messageId.Value,
                        replyMarkup: null,
                        cancellationToken: cancellationToken);
                }

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "✅ Оголошення опубліковано безкоштовно (адмін).",
                    cancellationToken: cancellationToken);
            }
            else
            {
                var code = await Program.PaymentService.GeneratePaymentCode(chatId, post);

                if (messageId.HasValue)
                {
                    await botClient.EditMessageReplyMarkupAsync(
                        chatId: chatId,
                        messageId: messageId.Value,
                        replyMarkup: null,
                        cancellationToken: cancellationToken);
                }

                var text =
                    $"💳 Щоб опублікувати оголошення, сплати 15 грн на банку:\n" +
                    $"👉 <a href=\"{_jarUrl}\">Натисни тут</a>\n\n" +
                    $"📝 У коментарі до платежу введи цей код: <code>{code}</code>\n\n" +
                    $"⏱ Після сплати бот автоматично перевірить оплату та опублікує оголошення впродовж 1–5 хвилин.\n" +
                    $"⏱ Оголошення на каналі автоматично видалиться через 3 дні";

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    parseMode: ParseMode.Html,
                    replyMarkup: KeyboardFactory.MainButtons(),
                    cancellationToken: cancellationToken);
            }
        }
        else if (callback.Data == "cancel")
        {
            var pending = await _pendingPaymentsService.GetLastByChatIdAsync(chatId);

            if (pending != null)
            {
                await _pendingPaymentsService.RemoveAsync(pending);
                await _postDraftSeevice.RemoveByChatIdAsync(pending.ChatId);
            }

            if (messageId.HasValue)
            {
                await botClient.EditMessageReplyMarkupAsync(
                    chatId: chatId,
                    messageId: messageId.Value,
                    replyMarkup: null,
                    cancellationToken: cancellationToken);
            }

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "❌ Публікацію скасовано.",
                replyMarkup: KeyboardFactory.MainButtons(),
                cancellationToken: cancellationToken);
        }
        else if (callback.Data.StartsWith("delete_post_msg_"))
        {
            var msgIdRaw = callback.Data.Replace("delete_post_msg_", "");
            if (!int.TryParse(msgIdRaw, out int msgIdToDelete))
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: "⚠️ Некоректний формат ID.",
                    cancellationToken: cancellationToken);
                return;
            }

            var postToRemove = await _confirmedPaymentsService.GetByChannelMessageIdAsync(msgIdToDelete);
            if (postToRemove == null)
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: "⚠️ Оголошення не знайдено.",
                    cancellationToken: cancellationToken);
                return;
            }

            bool isOwner = callback.From.Id == postToRemove.ChatId;
            bool isAdmin = callback.From.Id == _adminChatId;
            if (!isOwner && !isAdmin)
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: "⛔ Ви можете видалити лише свої оголошення.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                var channel = new ChatId("@baraholka_market_ua");
                await botClient.DeleteMessageAsync(
                    chatId: channel,
                    messageId: msgIdToDelete,
                    cancellationToken: cancellationToken);

                await _confirmedPaymentsService.RemoveAsync(postToRemove);
                var affected = await _postDraftSeevice.RemoveByChannelMessageIdAsync(msgIdToDelete);
                if (affected == 0)
                {
                    await _postDraftSeevice.RemoveByPostIdAsync(postToRemove.PostId);
                }

                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: "✅ Оголошення видалено.",
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                var shortError = ex.Message.Length > 180 ? ex.Message[..180] + "..." : ex.Message;
                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: $"❌ Помилка: {shortError}",
                    cancellationToken: cancellationToken);
            }
        }
    }





}

