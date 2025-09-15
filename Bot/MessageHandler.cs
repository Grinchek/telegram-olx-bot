using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Services;
using Services.Interfaces;
using Data.Entities;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
            var msg = update.Message;
            if (msg == null) return;

            // команди
            if (!string.IsNullOrEmpty(msg.Text) && msg.Text.StartsWith("/"))
            {
                await CommandHandler.HandleCommandAsync(botClient, msg, confirmedPaymentsService, _postDraftService, cancellationToken);
                return;
            }

            // кнопки
            if (msg.Text == "📤 Опублікувати оголошення")
            {
                var used = await _postCounterService.GetCurrentCountAsync();
                var left = 100 - used;
                var text = left <= 0
                    ? "❌ Денний ліміт публікацій вичерпано. Спробуй завтра."
                    : $"🔗 Надішли посилання з OLX/Shafa/Instagram.\n📊 Сьогодні: <b>{used}</b>/100, залишилось <b>{left}</b>.";
                await botClient.SendTextMessageAsync(
                     chatId: msg.Chat.Id,
                     text: text,
                     parseMode: ParseMode.Html,
                     replyMarkup: KeyboardFactory.MainButtons(),
                     cancellationToken: cancellationToken);
                return;
            }
            if (msg.Text == "📢 Перейти на канал")
            {
                await botClient.SendTextMessageAsync(
                    chatId: msg.Chat.Id,
                    text: "📬 <a href=\"https://t.me/+-90fie9HmXhhMjUy\">Перейти на канал</a>",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
                return;
            }

            // лінк
            var url = ExtractUrl(msg);
            if (string.IsNullOrWhiteSpace(url))
            {
                await botClient.SendTextMessageAsync(
                    chatId: msg.Chat.Id,
                    text: "⚠️ Надішли посилання на OLX, Shafa або Instagram (можна скористатися «Поділитися»).\nЯкщо що — користуйся кнопками нижче 👇",
                    parseMode: ParseMode.Html,
                    replyMarkup: KeyboardFactory.MainButtons(),
                    cancellationToken: cancellationToken);
                return;
            }

            await botClient.SendTextMessageAsync(msg.Chat.Id, "⏳ Парсинг оголошення...", cancellationToken: cancellationToken);

            try
            {
                PostData post =
                    Is(url, "olx") ? await OlxParser.ParseOlxAsync(url) :
                    Is(url, "shafa") ? await ShafaParser.ParseShafaAsync(url) :
                    /* insta */        await InstagramParser.ParseInstagramAsync(url);

                post.ImageUrl ??= "https://via.placeholder.com/300";
                await _postDraftService.SaveDraftAsync(msg.Chat.Id, post);
                await Program.PendingPaymentsService.AddAsync(new PendingPayment { ChatId = msg.Chat.Id, PostId = post.Id, RequestedAt = DateTime.UtcNow });

                var caption = CaptionBuilder.Build(post, false, _botUsername);
                var media = await InstagramMedia.BuildAsync(post.SourceUrl, post.ImageUrl);
                await InstagramMedia.SendAsync(botClient, msg.Chat.Id, media, caption, KeyboardFactory.ConfirmButtons(), cancellationToken);
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(msg.Chat.Id, $"⚠️ Помилка: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        // ===== helpers =====
        private static bool Is(string url, string key) => url.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string? ExtractUrl(Message m)
        {
            string? fromEnt(string? t, MessageEntity[]? e) =>
                e?.FirstOrDefault(x => x.Type is MessageEntityType.Url or MessageEntityType.TextLink) is { } me
                    ? (me.Type == MessageEntityType.TextLink ? me.Url : SafeSub(t, me.Offset, me.Length))
                    : null;

            var u = fromEnt(m.Text, m.Entities) ?? fromEnt(m.Caption, m.CaptionEntities) ?? FirstUrlLike(m.Text) ?? FirstUrlLike(m.Caption);
            return Normalize(u);
        }

        private static string Normalize(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url ?? "";
            var s = url.Trim();
            if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) s = "https://" + s;
            return s;
        }

        private static string? FirstUrlLike(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            var m = Regex.Match(t, @"https?://\S+|(?:www\.)?\S+\.\S+");
            return m.Success ? m.Value : null;
        }

        private static string? SafeSub(string? s, int off, int len)
        {
            if (string.IsNullOrEmpty(s) || off < 0 || len <= 0 || off >= s.Length) return null;
            len = Math.Min(len, s.Length - off);
            return s.Substring(off, len);
        }
    }
}
