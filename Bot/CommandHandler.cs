// /Bot/CommandHandler.cs
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading;
using System.Threading.Tasks;
using Storage;
using System;

namespace Bot;

public static class CommandHandler
{
    private static readonly long AdminChatId = long.Parse(Environment.GetEnvironmentVariable("ADMIN_CHAT_ID") ?? "0");
    private static readonly string BotUsername = Environment.GetEnvironmentVariable("BOT_USERNAME") ?? "@BARACHOLKA_UA_bot";


    public static async Task HandleCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var text = message.Text ?? "/start";
        
        if (text == "/start")
        {
            await botClient.SendTextMessageAsync(chatId, $"Ваш chatId: {chatId}", cancellationToken: cancellationToken);
            await botClient.SendTextMessageAsync(chatId,
                $"👋 Привіт! Ласкаво просимо до сервісу автоматичної публікації оголошень у Telegram-каналі!\n\n" +
                "📌 Що ти можеш зробити в цьому боті:\n" +
                "• 📤 Опублікувати оголошення з OLX — просто встав посилання в поле вводу.\n" +
                "• 📢 Перейти до каналу, де буде опубліковане твоє оголошення.\n" +
                "• 🗑 Видалити своє оголошення після публікації (є кнопка під постом).\n\n" +
                "❗ Якщо хочеш опублікувати оголошення НЕ з OLX — або маєш питання — напиши сюди 👉 @Ad_min_OLX_Bot\n\n" +
                "👇 Обери дію нижче:",
                replyMarkup: KeyboardFactory.MainButtons(),
                cancellationToken: cancellationToken);

            return;
        }


        if (text.StartsWith("/delete") && chatId == AdminChatId)
        {
            var parts = text.Split(' ');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var messageId))
            {
                await botClient.SendTextMessageAsync(chatId, "❌ Вкажи ID повідомлення. Наприклад:\n`/delete 123`", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                return;
            }

            var posts = ConfirmedPayments.GetAll();
            var post = posts.FirstOrDefault(p => p.Post?.ChannelMessageId == messageId);

            if (post == null)
            {
                await botClient.SendTextMessageAsync(chatId, "⚠️ Пост із таким ID не знайдено.", cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await botClient.DeleteMessageAsync("@baraholka_market_ua", messageId, cancellationToken);
                posts.Remove(post);
                ConfirmedPayments.Save();

                await botClient.SendTextMessageAsync(chatId, "✅ Оголошення видалено.", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(chatId, $"❌ Помилка при видаленні: {ex.Message}", cancellationToken: cancellationToken);
            }
        }
        else
        {
            await botClient.SendTextMessageAsync(chatId, "⚠️ Невідома команда або недостатньо прав.", cancellationToken: cancellationToken);
        }
    }
}
