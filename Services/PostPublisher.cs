using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Data.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bot;

namespace Services
{
    public class PostPublisher
    {
        private readonly ITelegramBotClient _bot;

        public PostPublisher(ITelegramBotClient botClient) => _bot = botClient;

        public async Task<int> PublishPostAsync(PostData post, long originalChatId, bool isAdmin, CancellationToken ct = default)
        {
            if (post == null) throw new ArgumentNullException(nameof(post));

            var caption = CaptionBuilder.Build(post, isAdmin, Program.BotUsername);
            post.ImageUrl ??= "https://via.placeholder.com/300";
            var channel = Program.ChannelUsername;

            var media = await InstagramMedia.BuildAsync(post.SourceUrl, post.ImageUrl);

            // Надсилаємо з тимчасовою клавіатурою (messageId невідомий)
            var dummyMarkup = KeyboardFactory.DeleteButtonByMessageId(0);
            Message sent;

            switch (media.Kind)
            {
                case "video":
                    await using (var ms = new System.IO.MemoryStream(media.Bytes!))
                        sent = await _bot.SendVideoAsync(channel, InputFile.FromStream(ms, media.FileName),
                            caption: caption, parseMode: ParseMode.Html, replyMarkup: dummyMarkup, supportsStreaming: true, cancellationToken: ct);
                    break;
                case "photo":
                    await using (var ms = new System.IO.MemoryStream(media.Bytes!))
                        sent = await _bot.SendPhotoAsync(channel, InputFile.FromStream(ms, media.FileName),
                            caption: caption, parseMode: ParseMode.Html, replyMarkup: dummyMarkup, cancellationToken: ct);
                    break;
                case "document":
                    await using (var ms = new System.IO.MemoryStream(media.Bytes!))
                        sent = await _bot.SendDocumentAsync(channel, InputFile.FromStream(ms, media.FileName),
                            caption: caption, parseMode: ParseMode.Html, replyMarkup: dummyMarkup, cancellationToken: ct);
                    break;
                default:
                    sent = await _bot.SendPhotoAsync(channel, InputFile.FromUri(media.DirectUrl!),
                            caption: caption, parseMode: ParseMode.Html, replyMarkup: dummyMarkup, cancellationToken: ct);
                    break;
            }

            // оновлюємо клавіатуру, коли вже знаємо messageId
            post.ChannelMessageId = sent.MessageId;
            await _bot.EditMessageReplyMarkupAsync(channel, sent.MessageId, KeyboardFactory.DeleteButtonByMessageId(sent.MessageId), cancellationToken: ct);

            return sent.MessageId;
        }
    }
}
