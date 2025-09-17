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

        // ==== Нове: перелік платформ та детектор доменів ====
        private enum PlatformType
        {
            Unknown = 0,
            Olx,
            Shafa,
            Kidstaff,
            BesplatkaOrBon,
        }

        private static PlatformType DetectPlatform(string? url)
        {
            if (IsOlxUrl(url)) return PlatformType.Olx;
            if (IsShafaUrl(url)) return PlatformType.Shafa;
            if (IsKidstaffUrl(url)) return PlatformType.Kidstaff;
            if (IsBesplatkaOrBonUrl(url)) return PlatformType.BesplatkaOrBon;
            return PlatformType.Unknown;
        }

        private static string PlatformName(PlatformType p) => p switch
        {
            PlatformType.Olx => "OLX",
            PlatformType.Shafa => "Shafa",
            PlatformType.Kidstaff => "KidStaff",
            PlatformType.BesplatkaOrBon => "Besplatka (BON.ua)",
            _ => "Невідома платформа"
        };

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
                        $"🔗 Надішли оголошення з <b>OLX</b>, <b>Shafa</b>, <b>KidStaff</b> або <b>Besplatka (BON.ua)</b> у будь-який зручний спосіб:\n\n" +
                        $"• Встав посилання вручну, або\n" +
                        $"• Поділися оголошенням через кнопку \"Поділитися\" у застосунку/на сайті.\n\n" +
                        $"ℹ️ Лінки <b>crafta.ua</b> обробляються як Shafa.\n\n" +
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

            // 3) Спроба дістати URL з повідомлення (будь-який домен)
            var anyUrl = ExtractUrl(message);
            if (!string.IsNullOrEmpty(anyUrl))
            {
                var platform = DetectPlatform(anyUrl);

                try
                {
                    PostData postData;

                    switch (platform)
                    {
                        case PlatformType.Olx:
                            await botClient.SendTextMessageAsync(chatId, "⏳ Парсинг оголошення з OLX...", cancellationToken: cancellationToken);
                            postData = await OlxParser.ParseOlxAsync(anyUrl!);
                            break;

                        case PlatformType.Shafa:
                            await botClient.SendTextMessageAsync(chatId, "⏳ Парсинг оголошення з Shafa...", cancellationToken: cancellationToken);
                            postData = await ShafaParser.ParseShafaAsync(anyUrl!);
                            break;

                        case PlatformType.Kidstaff:
                            await botClient.SendTextMessageAsync(chatId, "⏳ Парсинг оголошення з KidStaff...", cancellationToken: cancellationToken);
                            postData = await KidstaffParser.ParseAsync(anyUrl!);
                            break;

                        case PlatformType.BesplatkaOrBon:
                            await botClient.SendTextMessageAsync(chatId, "⏳ Парсинг оголошення з Besplatka/BON...", cancellationToken: cancellationToken);
                            postData = await BesplatkaParser.ParseAsync(anyUrl!);
                            break;

                        default:
                            await botClient.SendTextMessageAsync(
                                chatId,
                                "⚠️ Невідоме або непідтримуване посилання.\n" +
                                "Підтримуються: <b>OLX</b>, <b>Shafa</b>, <b>KidStaff</b>, <b>Besplatka (BON.ua)</b>.\n" +
                                "Якщо це був старий лінк Crafta/IZI — вони зараз працюють як Shafa. " +
                                "Спробуй відкрити в браузері та надіслати кінцеву адресу.",
                                parseMode: ParseMode.Html,
                                cancellationToken: cancellationToken);
                            return;

                    }

                    // Плейсхолдер, якщо не знайшли зображення
                    postData.ImageUrl ??= "https://via.placeholder.com/300";

                    // Зберігаємо драфт і створюємо очікувану публікацію
                    await _postDraftService.SaveDraftAsync(chatId, postData);

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
            await botClient.SendTextMessageAsync(
                chatId,
                "⚠️ Надішли посилання на <b>OLX</b>, <b>Shafa</b>, <b>KidStaff</b> або <b>Besplatka (BON.ua)</b> (можна скористатися «Поділитися»).\n" +
                "ℹ️ Лінки <b>crafta.ua</b> обробляються як Shafa.\n" +
                "Якщо що — користуйся кнопками нижче 👇",
                parseMode: ParseMode.Html,
                replyMarkup: KeyboardFactory.MainButtons(),
                cancellationToken: cancellationToken
            );

        }


        // Витягуємо будь-який URL з тексту/ентиті/підпису і нормалізуємо
        private static string? ExtractUrl(Message message)
        {
            var fromText = ExtractFromTextAndEntities(message.Text, message.Entities);
            if (!string.IsNullOrWhiteSpace(fromText)) return NormalizeUrl(fromText);

            var fromCaption = ExtractFromTextAndEntities(message.Caption, message.CaptionEntities);
            if (!string.IsNullOrWhiteSpace(fromCaption)) return NormalizeUrl(fromCaption);

            var any = FirstUrlLike(message.Text) ?? FirstUrlLike(message.Caption);
            if (!string.IsNullOrWhiteSpace(any)) return NormalizeUrl(any);

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
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

                if (!string.IsNullOrEmpty(urlFromEntities))
                    return urlFromEntities;
            }

            // Якщо ентиті не дали URL — перевіримо, чи весь текст є URL або містить URL
            var any = FirstUrlLike(text);
            if (!string.IsNullOrWhiteSpace(any)) return any;

            // Повертаємо текст як є (може бути "www..." без схеми — дозбираємо далі)
            return text;
        }

        private static string? SafeSubstring(string source, int offset, int length)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (offset < 0 || length <= 0 || offset >= source.Length) return null;
            var maxLen = Math.Min(length, source.Length - offset);
            return source.Substring(offset, maxLen);
        }

        // ==== Перевірки доменів ====
        private static bool IsOlxUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            var candidate = url.Trim();
            candidate = FirstUrlLike(candidate) ?? candidate;

            if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                candidate = "https://" + candidate;
            }

            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                   && uri.Host.Contains("olx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsShafaUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var candidate = FirstUrlLike(url.Trim()) ?? url.Trim();

            if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                candidate = "https://" + candidate;
            }

            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                   && uri.Host.Contains("shafa.ua", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKidstaffUrl(string? url)
        {
            var u = NormalizeUrl(url);
            return Uri.TryCreate(u, UriKind.Absolute, out var uri)
                   && uri.Host.Contains("kidstaff.com.ua", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBesplatkaOrBonUrl(string? url)
        {
            var u = NormalizeUrl(url);
            return Uri.TryCreate(u, UriKind.Absolute, out var uri)
                   && (uri.Host.Contains("besplatka.ua", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.Contains("bon.ua", StringComparison.OrdinalIgnoreCase));
        }

        // ==== Утиліти URL ====
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
