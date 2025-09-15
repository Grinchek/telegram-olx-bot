using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Data.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bot;

namespace Services;

public class PostPublisher
{
    private readonly ITelegramBotClient _botClient;

    public PostPublisher(ITelegramBotClient botClient)
    {
        _botClient = botClient;
    }

    public async Task<int> PublishPostAsync(PostData post, long originalChatId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (post == null) throw new ArgumentNullException(nameof(post));

        var caption = CaptionBuilder.Build(post, isAdmin, Program.BotUsername);
        var imageUrl = string.IsNullOrWhiteSpace(post.ImageUrl)
            ? "https://via.placeholder.com/300"
            : post.ImageUrl;

        // Єдине джерело правди для каналу
        var channel = Program.ChannelUsername;

        try
        {
            // Надсилаємо фото одразу з кнопкою "Видалити", без додаткового редагування
            var result = await _botClient.SendPhotoAsync(
                chatId: channel,
                photo: InputFile.FromUri(imageUrl),
                caption: caption,
                parseMode: ParseMode.Html,
                replyMarkup: KeyboardFactory.DeleteButtonByMessageId(0), // тимчасово 0, замінимо нижче, Telegram дозволяє лише під час Edit
                cancellationToken: cancellationToken
            );

            // Telegram не знає messageId в момент побудови клавіатури, тому оновимо markup після відправки
            post.ChannelMessageId = result.MessageId;

            await _botClient.EditMessageReplyMarkupAsync(
                chatId: channel,
                messageId: result.MessageId,
                replyMarkup: KeyboardFactory.DeleteButtonByMessageId(result.MessageId),
                cancellationToken: cancellationToken
            );

            return result.MessageId;
        }
        catch (Exception ex)
        {
            // Можеш замінити на власний логер
            Console.WriteLine($"❌ Помилка публікації поста: {ex.Message}");
            throw;
        }
    }
}
