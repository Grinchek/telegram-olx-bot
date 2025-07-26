// /Bot/MessageHandler.cs
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Services;
using Storage;
using Models;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Bot
{
    public static class MessageHandler
    {
        private static readonly string BotUsername = Environment.GetEnvironmentVariable("BOT_USERNAME") ?? "@BARACHOLKA_UA_bot";

        public static async Task HandleMessageAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var message = update.Message;
            if (message == null || message.Text == null)
                return;

            var chatId = message.Chat.Id;
            var text = message.Text;
            if (message.Text.StartsWith("/"))
            {
                await CommandHandler.HandleCommandAsync(botClient, message, cancellationToken);
                return;
            }
            else if (text == "📤 Опублікувати оголошення")
            {
                var current = PostCounter.GetCurrentCount();
                var remaining = 100 - current;

                await botClient.SendTextMessageAsync(chatId,
                    $"🔗 Надішли посилання на оголошення з OLX:\n\n" +
                    $"📊 Сьогодні вже опубліковано: <b>{current}</b>/100\n" +
                    $"🕐 Залишилось місць: <b>{remaining}</b>\n\n" +
                    $"‼️ Встав саме <u>посилання</u> вручну в поле вводу, а не ділись ним через “Поділитися” — бот не зможе його розпізнати.",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }


            else if (text == "📢 Перейти на канал")
            {
                await botClient.SendTextMessageAsync(chatId, "📬 <a href=\"https://t.me/baraholka_market_ua\">Перейти на канал</a>", parseMode: ParseMode.Html, cancellationToken: cancellationToken);
            }
            else if (text.StartsWith("http") && text.Contains("olx"))
            {
                await botClient.SendTextMessageAsync(chatId, "⏳ Парсинг оголошення...", cancellationToken: cancellationToken);
                try
                {
                    var post = await OlxParser.ParseOlxAsync(text);
                    var postData = new PostData(post.Title, post.Price, post.Description, post.ImageUrl, text);
                    InMemoryRepository.PendingPosts[chatId] = postData;

                    var caption = CaptionBuilder.Build(postData, false, BotUsername);

                    await botClient.SendPhotoAsync(
                        chatId,
                        InputFile.FromUri(post.ImageUrl ?? "https://via.placeholder.com/300"),
                        caption: caption,
                        parseMode: ParseMode.Html,
                        replyMarkup: KeyboardFactory.ConfirmButtons(),
                        cancellationToken: cancellationToken);

                }
                catch (Exception ex)
                {
                    await botClient.SendTextMessageAsync(chatId, $"⚠️ Помилка: {ex.Message}", cancellationToken: cancellationToken);
                }
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "⚠️ Будь ласка, користуйся кнопками нижче 👇", replyMarkup: KeyboardFactory.MainButtons(), cancellationToken: cancellationToken);
            }
        }
    }
}
