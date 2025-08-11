using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Data.Entities;
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
        var caption = CaptionBuilder.Build(post, isAdmin, Program.BotUsername);
        var imageUrl = post.ImageUrl ?? "https://via.placeholder.com/300";


        var result = await _botClient.SendPhotoAsync(
            chatId: Program.ChannelUsername,
            photo: InputFile.FromUri(imageUrl),
            caption: caption,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken
        );

        post.ChannelMessageId = result.MessageId;

        await _botClient.EditMessageReplyMarkupAsync(
            chatId: Program.ChannelUsername,
            messageId: result.MessageId,
            replyMarkup: KeyboardFactory.DeleteButtonByMessageId(result.MessageId),
            cancellationToken: cancellationToken
        );


        return result.MessageId;
    }



}
