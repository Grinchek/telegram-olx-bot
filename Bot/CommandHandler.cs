// /Bot/CommandHandler.cs
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading;
using System.Threading.Tasks;
using Services.Interfaces;
using System;

namespace Bot;

public static class CommandHandler
{
    private static readonly long AdminChatId = long.Parse(Environment.GetEnvironmentVariable("ADMIN_CHAT_ID") ?? "0");
    private static readonly string BotUsername = Environment.GetEnvironmentVariable("BOT_USERNAME") ?? "@BARACHOLKA_UA_bot";


    public static async Task HandleCommandAsync(
    ITelegramBotClient botClient,
    Message message,
    IConfirmedPaymentsService confirmedPaymentsService,
    IPostDraftService postDraftSeevice,
    CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var text = message.Text ?? "/start";

        if (text == "/start")
        {
            await botClient.SendTextMessageAsync(chatId,
                $"👋 Привіт! Ласкаво просимо...",
                replyMarkup: KeyboardFactory.MainButtons(),
                cancellationToken: cancellationToken);
            return;
        }

        if (text.StartsWith("/delete") && chatId == AdminChatId)
        {
            var parts = text.Split(' ');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var messageId))
            {
                await botClient.SendTextMessageAsync(chatId,
                    "❌ Вкажи ID повідомлення. Наприклад:\n`/delete 123`",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            var posts = await confirmedPaymentsService.GetAllAsync();
            var post = posts.FirstOrDefault(p => p.Post?.ChannelMessageId == messageId);

            if (post == null)
            {
                await botClient.SendTextMessageAsync(chatId, "⚠️ Пост із таким ID не знайдено.", cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await botClient.DeleteMessageAsync("@baraholka_market_ua", messageId, cancellationToken);

                // Видалення з бази
                await confirmedPaymentsService.RemoveAsync(post);
                await postDraftSeevice.RemoveByChatIdAsync(post.ChatId);
                await botClient.SendTextMessageAsync(chatId, "✅ Оголошення видалено.", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(chatId, $"❌ Помилка при видаленні: {ex.Message}", cancellationToken: cancellationToken);
            }
            return;
        }

        await botClient.SendTextMessageAsync(chatId, "⚠️ Невідома команда або недостатньо прав.", cancellationToken: cancellationToken);
    }

}
