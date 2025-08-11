////// /Bot/MessageHandler.cs
//using Telegram.Bot;
//using Telegram.Bot.Types;
//using Telegram.Bot.Types.Enums;
//using Services;
//using Services.Interfaces;
//using Data.Entities;
//using Telegram.Bot.Types.ReplyMarkups;
//using System.Threading.Tasks;
//using System.Threading;
//using System;


//namespace Bot
//{
//    public class MessageHandler
//    {
//        private readonly IPostDraftService _postDraftService;
//        private readonly IPostCounterService _postCounterService;
//        private readonly string _botUsername;

//        public MessageHandler(IPostDraftService postDraftService, IPostCounterService postCounterService)
//        {
//            _postDraftService = postDraftService;
//            _postCounterService = postCounterService;
//            _botUsername = Environment.GetEnvironmentVariable("BOT_USERNAME") ?? "@BARACHOLKA_UA_bot";
//        }

//        public async Task HandleMessageAsync(
//            ITelegramBotClient botClient,
//            Update update,
//            IConfirmedPaymentsService confirmedPaymentsService,
//            CancellationToken cancellationToken)
//        {
//            var message = update.Message;
//            if (message == null || message.Text == null)
//                return;

//            var chatId = message.Chat.Id;
//            var text = message.Text;

//            if (text.StartsWith("/"))
//            {
//                await CommandHandler.HandleCommandAsync(botClient, message, confirmedPaymentsService, _postDraftService, cancellationToken);
//                return;
//            }

//            if (text == "📤 Опублікувати оголошення")
//            {
//                var current = await _postCounterService.GetCurrentCountAsync();
//                var remaining = 100 - current;

//                await botClient.SendTextMessageAsync(chatId,
//                    $"🔗 Надішли посилання на оголошення з OLX:\n\n" +
//                    $"📊 Сьогодні вже опубліковано: <b>{current}</b>/100\n" +
//                    $"🕐 Залишилось місць: <b>{remaining}</b>\n\n" +
//                    $"‼️ Встав саме <u>посилання</u> вручну в поле вводу, а не ділись ним через “Поділитися” — бот не зможе його розпізнати.",
//                    parseMode: ParseMode.Html,
//                    cancellationToken: cancellationToken);
//            }
//            else if (text == "📢 Перейти на канал")
//            {
//                await botClient.SendTextMessageAsync(chatId,
//                    "📬 <a href=\"https://t.me/baraholka_market_ua\">Перейти на канал</a>",
//                    parseMode: ParseMode.Html,
//                    cancellationToken: cancellationToken);
//            }
//            else if (text.StartsWith("http") && text.Contains("olx"))
//            {
//                await botClient.SendTextMessageAsync(chatId, "⏳ Парсинг оголошення...", cancellationToken: cancellationToken);

//                try
//                {
//                    var postData = await OlxParser.ParseOlxAsync(text);

//                    // Зберігаємо в чернетки
//                    await _postDraftService.SaveDraftAsync(chatId, postData);

//                    // Додаємо до очікуваних платежів
//                    await Program.PendingPaymentsService.AddAsync(new PendingPayment
//                    {
//                        ChatId = chatId,
//                        PostId = postData.Id,
//                        RequestedAt = DateTime.UtcNow
//                    });


//                    var caption = CaptionBuilder.Build(postData, false, _botUsername);

//                    await botClient.SendPhotoAsync(
//                        chatId,
//                        InputFile.FromUri(postData.ImageUrl ?? "https://via.placeholder.com/300"),
//                        caption: caption,
//                        parseMode: ParseMode.Html,
//                        replyMarkup: KeyboardFactory.ConfirmButtons(),
//                        cancellationToken: cancellationToken);
//                }
//                catch (Exception ex)
//                {
//                    await botClient.SendTextMessageAsync(chatId, $"⚠️ Помилка: {ex.Message}", cancellationToken: cancellationToken);
//                }
//            }

//            else
//            {
//                await botClient.SendTextMessageAsync(chatId,
//                    "⚠️ Будь ласка, користуйся кнопками нижче 👇",
//                    replyMarkup: KeyboardFactory.MainButtons(),
//                    cancellationToken: cancellationToken);
//            }
//        }
//    }
//}
//// /Bot/MessageHandler.cs
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Services;
using Services.Interfaces;
using Data.Entities;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bot
{
    public class MessageHandler
    {
        private readonly IPostDraftService _postDraftService;
        private readonly IPostCounterService _postCounterService;
        private readonly string _botUsername;

        public MessageHandler(IPostDraftService postDraftService, IPostCounterService postCounterService)
        {
            _postDraftService = postDraftService;
            _postCounterService = postCounterService;
            _botUsername = Environment.GetEnvironmentVariable("BOT_USERNAME") ?? "@BARACHOLKA_UA_bot";
        }

        public async Task HandleMessageAsync(
            ITelegramBotClient botClient,
            Update update,
            IConfirmedPaymentsService confirmedPaymentsService,
            CancellationToken cancellationToken)
        {
            var message = update.Message;
            if (message == null)
                return;

            var chatId = message.Chat.Id;
            var text = message.Text;

            // 1) Команди
            if (!string.IsNullOrEmpty(text) && text.StartsWith("/"))
            {
                await CommandHandler.HandleCommandAsync(botClient, message, confirmedPaymentsService, _postDraftService, cancellationToken);
                return;
            }

            // 2) Кнопки головного меню
            if (text == "📤 Опублікувати оголошення")
            {
                var current = await _postCounterService.GetCurrentCountAsync();
                var remaining = 100 - current;

                if (remaining <= 0)
                {
                    await botClient.SendTextMessageAsync(chatId,
                        "❌ Денний ліміт публікацій вичерпано. Спробуй завтра.",
                        parseMode: ParseMode.Html,
                        replyMarkup: KeyboardFactory.MainButtons(),
                        cancellationToken: cancellationToken);
                    return;
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId,
                        $"🔗 Надішли оголошення з OLX у будь-який зручний спосіб:\n\n" +
                        $"• Встав посилання вручну, або\n" +
                        $"• Поділися оголошенням через кнопку \"Поділитися\" в OLX.\n\n" +
                        $"📊 Сьогодні вже опубліковано: <b>{current}</b>/100\n" +
                        $"🕐 Залишилось місць: <b>{remaining}</b>",
                        parseMode: ParseMode.Html,
                        replyMarkup: KeyboardFactory.MainButtons(),
                        cancellationToken: cancellationToken);
                    return;

                }
                    
            }
            else if (text == "📢 Перейти на канал")
            {
                await botClient.SendTextMessageAsync(chatId,
                    "📬 <a href=\"https://t.me/baraholka_market_ua\">Перейти на канал</a>",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
                return;
            }

            // 3) Спроба дістати OLX‑URL з будь-якого типу повідомлення (текст, підпис до фото/відео, «Поділитися» тощо)
            var olxUrl = ExtractOlxUrl(message);
            if (!string.IsNullOrEmpty(olxUrl))
            {
                await botClient.SendTextMessageAsync(chatId, "⏳ Парсинг оголошення...", cancellationToken: cancellationToken);

                try
                {
                    var postData = await OlxParser.ParseOlxAsync(olxUrl!);

                    // Зберігаємо в чернетки
                    await _postDraftService.SaveDraftAsync(chatId, postData);

                    // Додаємо до очікуваних платежів
                    await Program.PendingPaymentsService.AddAsync(new PendingPayment
                    {
                        ChatId = chatId,
                        PostId = postData.Id,
                        RequestedAt = DateTime.UtcNow
                    });

                    var caption = CaptionBuilder.Build(postData, false, _botUsername);

                    await botClient.SendPhotoAsync(
                        chatId,
                        InputFile.FromUri(postData.ImageUrl ?? "https://via.placeholder.com/300"),
                        caption: caption,
                        parseMode: ParseMode.Html,
                        replyMarkup: KeyboardFactory.ConfirmButtons(),
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    await botClient.SendTextMessageAsync(chatId, $"⚠️ Помилка: {ex.Message}", cancellationToken: cancellationToken);
                }

                return;
            }

            // 4) Фолбек — підказка користувачу
            await botClient.SendTextMessageAsync(chatId,
                "⚠️ Надішли посилання на OLX або скористайся кнопкою ‘Поділитися’ в OLX.\nЯкщо що — користуйся кнопками нижче 👇",
                replyMarkup: KeyboardFactory.MainButtons(),
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Витягує перший валідний OLX‑URL з повідомлення у різних сценаріях:
        ///  - звичайний текст (включно з додатковим описом)
        ///  - посилання у текстових ентитях (Url / TextLink)
        ///  - підпис до медіа (caption / caption_entities)
        ///  - «Поділитися» з OLX (зазвичай приходить як текст з превʼю)
        /// </summary>
        private static string? ExtractOlxUrl(Message message)
        {
            // 1) Перевіряємо текст + ентиті
            var fromText = ExtractFromTextAndEntities(message.Text, message.Entities);
            if (IsOlxUrl(fromText)) return NormalizeUrl(fromText);

            // 2) Перевіряємо підпис до медіа + ентиті підпису
            var fromCaption = ExtractFromTextAndEntities(message.Caption, message.CaptionEntities);
            if (IsOlxUrl(fromCaption)) return NormalizeUrl(fromCaption);

            // 3) Фолбек: regex по тексту і підпису
            var any = FirstUrlLike(message.Text) ?? FirstUrlLike(message.Caption);
            if (IsOlxUrl(any)) return NormalizeUrl(any);

            return null;
        }

        private static string? ExtractFromTextAndEntities(string? text, MessageEntity[]? entities)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (entities != null && entities.Length > 0)
            {
                // Пріоритет: явні ентиті URL або TextLink
                var urlFromEntities = entities
                    .Where(e => e.Type == MessageEntityType.Url || e.Type == MessageEntityType.TextLink)
                    .Select(e => e.Type == MessageEntityType.TextLink
                        ? e.Url
                        : SafeSubstring(text, e.Offset, e.Length))
                    .FirstOrDefault(u => IsOlxUrl(u));
                if (!string.IsNullOrEmpty(urlFromEntities)) return urlFromEntities;
            }
            return text; // повертаємо як є — далі відфільтруємо
        }

        private static string? SafeSubstring(string source, int offset, int length)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (offset < 0 || length <= 0 || offset >= source.Length) return null;
            var maxLen = Math.Min(length, source.Length - offset);
            return source.Substring(offset, maxLen);
        }

        private static bool IsOlxUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            var candidate = url.Trim();
            // Якщо це шматок тексту — дістанемо перший URL‑підрядок
            candidate = FirstUrlLike(candidate) ?? candidate;

            if (!candidate.Contains("olx", StringComparison.OrdinalIgnoreCase)) return false;

            // Додаткова валідація URL
            if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                candidate = "https://" + candidate;
            }

            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                   && (uri.Host.Contains("olx", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url ?? string.Empty;
            var candidate = FirstUrlLike(url) ?? url.Trim();
            if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                candidate = "https://" + candidate;
            }
            return candidate;
        }

        private static string? FirstUrlLike(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Matches(text, @"https?://[^\s]+|(?:www\.)?[^\s]+\.[^\s]+")
                         .Cast<Match>()
                         .Select(x => x.Value)
                         .FirstOrDefault();
            return m;
        }
    }
}

