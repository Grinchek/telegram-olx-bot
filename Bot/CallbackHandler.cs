using System;
using System.Threading.Tasks;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Services.Interfaces;
using Data.Entities;
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
        // Temporary free publish implementation
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
            bool isAdmin = callback.From.Id == _adminChatId;

            // Перевірка підписки (адміну дозволяємо завжди)
            bool isSubscribed = isAdmin ||
                await IsSubscribedAsync(botClient, Program.ChannelUsername, callback.From.Id, cancellationToken);

            if (!isSubscribed)
            {
                // Кнопка з переходом на канал + кнопка повторної перевірки
                var url = $"https://t.me/{Program.ChannelUsername.TrimStart('@')}";
                var kb = new InlineKeyboardMarkup(new[]
                {
            new[]
            {
                InlineKeyboardButton.WithUrl("🔔 Підписатися на канал", url)
            },
            new[]
            {
                // Та ж сама callback-дія, щоб користувач натиснув і ми перевірили ще раз
                InlineKeyboardButton.WithCallbackData("✅ Перевірити підписку", "confirm_publish")
            }
        });

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Щоб опублікувати оголошення, спочатку підпишіться на наш канал 🙂",
                    replyMarkup: kb,
                    cancellationToken: cancellationToken);

                // Можна прибрати кнопки під старим повідомленням
                if (messageId.HasValue)
                {
                    await botClient.EditMessageReplyMarkupAsync(
                        chatId: chatId,
                        messageId: messageId.Value,
                        replyMarkup: null,
                        cancellationToken: cancellationToken);
                }

                return;
            }

            // --- тут логіка безкоштовної публікації для підписників/адміна ---
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
                text: "✅ Оголошення опубліковано.",
                cancellationToken: cancellationToken);
        }

        // Paid implementation is currently disabled
        //if (callback.Data == "confirm_publish")
        //{
        //    var pending = await _pendingPaymentsService.GetLastByChatIdAsync(chatId);
        //    if (pending == null || pending.Post == null)
        //    {
        //        await botClient.AnswerCallbackQueryAsync(
        //            callbackQueryId: callback.Id,
        //            text: "⛔ Немає оголошення для публікації.",
        //            cancellationToken: cancellationToken);
        //        return;
        //    }

        //    var post = pending.Post!;
        //    bool isAdmin = chatId == _adminChatId;


        //    if (isAdmin)

        //    {
        //        var publishedMsgId = await _postPublisher.PublishPostAsync(post, chatId, true, cancellationToken);
        //        post.PublishedAt = DateTime.UtcNow;
        //        post.ChannelMessageId = publishedMsgId;

        //        var confirmed = new ConfirmedPayment
        //        {
        //            ChatId = chatId,
        //            Code = "FREE",
        //            Post = post,
        //            RequestedAt = DateTime.UtcNow,
        //            TransactionId = null
        //        };

        //        await _confirmedPaymentsService.AddAsync(confirmed);
        //        await _pendingPaymentsService.RemoveAsync(pending);

        //        if (messageId.HasValue)
        //        {
        //            await botClient.EditMessageReplyMarkupAsync(
        //                chatId: chatId,
        //                messageId: messageId.Value,
        //                replyMarkup: null,
        //                cancellationToken: cancellationToken);
        //        }

        //        await botClient.SendTextMessageAsync(
        //            chatId: chatId,
        //            text: "✅ Оголошення опубліковано безкоштовно (адмін).",
        //            cancellationToken: cancellationToken);
        //    }

        //    else
        //    {


        //        var code = await Program.PaymentService.GeneratePaymentCode(chatId, post);

        //        if (messageId.HasValue)
        //        {
        //            await botClient.EditMessageReplyMarkupAsync(
        //                chatId: chatId,
        //                messageId: messageId.Value,
        //                replyMarkup: null,
        //                cancellationToken: cancellationToken);
        //        }

        //        var text =
        //            $"💳 Щоб опублікувати оголошення, спcdлати 15 грн на банку:\n" +
        //            $"👉 <a href=\"{_jarUrl}\">Натисни тут</a>\n\n" +
        //            $"📝 У коментарі до платежу введи цей код: <code>{code}</code>\n\n" +
        //            $"⏱ Після сплати бот автоматично перевірить оплату та опублікує оголошення впродовж 1–5 хвилин.\n" +
        //            $"⏱ Оголошення на каналі автоматично видалиться через 72 години";

        //        await botClient.SendTextMessageAsync(
        //            chatId: chatId,
        //            text: text,
        //            parseMode: ParseMode.Html,
        //            replyMarkup: KeyboardFactory.MainButtons(),
        //            cancellationToken: cancellationToken);
        //
        //    }
        //}
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

            bool isAdmin = callback.From.Id == _adminChatId;
            bool canDelete = isAdmin || (postToRemove != null && callback.From.Id == postToRemove.ChatId);

            if (!canDelete)
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: "⛔ Ви можете видалити лише свої оголошення.",
                    cancellationToken: cancellationToken);
                return;
            }

            Console.WriteLine($"[DELETE_BTN] request by {callback.From.Id}, msgId={msgIdToDelete}, postId={(postToRemove?.PostId.ToString() ?? "null")}");

            try
            {
                var channel = new ChatId(Program.ChannelUsername);
                await botClient.DeleteMessageAsync(
                    chatId: channel,
                    messageId: msgIdToDelete,
                    cancellationToken: cancellationToken);

                Console.WriteLine($"[DELETE_BTN] TG deleted msgId={msgIdToDelete} OK");

                if (postToRemove != null)
                {
                    await _confirmedPaymentsService.RemoveAsync(postToRemove);
                    Console.WriteLine($"[DELETE_BTN] DB removed ConfirmedPaymentId={postToRemove.Id}");

                    try
                    {
                        var affected = await _postDraftSeevice.RemoveByChannelMessageIdAsync(msgIdToDelete);
                        Console.WriteLine($"[DELETE_BTN] DB drafts removed by msgId affected={affected}");
                        if (affected == 0)
                        {
                            await _postDraftSeevice.RemoveByPostIdAsync(postToRemove.PostId);
                            Console.WriteLine($"[DELETE_BTN] DB drafts removed by postId={postToRemove.PostId}");
                        }
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"[DELETE_BTN] WARN drafts cleanup failed: {ex2.Message}");
                    }
                }

                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: "✅ Оголошення видалено.",
                    cancellationToken: cancellationToken);
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            {
                var text = ex.Message.Contains("can't be deleted", StringComparison.OrdinalIgnoreCase)
                    ? "⚠️ Пост старіший за 48 год або немає прав — Telegram не дозволяє боту його видалити."
                    : $"⛔ Не вдалося видалити пост: {ex.Message}";

                Console.WriteLine($"[DELETE_BTN] TG delete FAIL msgId={msgIdToDelete}: {ex.Message}");
                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: text,
                    cancellationToken: cancellationToken);

                // БД не чіпаємо!
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE_BTN] UNEXPECTED err msgId={msgIdToDelete}: {ex}");
                await botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callback.Id,
                    text: $"⛔ Помилка видалення: {ex.Message}",
                    cancellationToken: cancellationToken);
                // БД не чіпаємо!
                return;
            }
        }



    }
    #region Subscription Check
    private static async Task<bool> IsSubscribedAsync(
    ITelegramBotClient bot,
    string channelUsername,
    long userId,
    CancellationToken ct)
    {
        try
        {
            var member = await bot.GetChatMemberAsync(new ChatId(channelUsername), userId, ct);

            // Перевіряємо конкретні підтипи ChatMember:
            return member switch
            {
                Telegram.Bot.Types.ChatMemberOwner => true, // власник каналу
                Telegram.Bot.Types.ChatMemberAdministrator => true, // адміністратор
                Telegram.Bot.Types.ChatMemberMember => true, // звичайний учасник
                Telegram.Bot.Types.ChatMemberRestricted r => r.IsMember, // restricted, але залишається учасником
                _ => false // Left / Banned / інше
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400 || ex.Message.Contains("user not found"))
        {
            // 400 Bad Request / user not found — не підписаний
            return false;
        }
        catch
        {
            // На будь-яку іншу помилку — вважаємо, що не підписаний
            return false;
        }
    }

    #endregion
}
