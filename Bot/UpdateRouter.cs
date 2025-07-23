// /Bot/UpdateRouter.cs
using Telegram.Bot;
using Telegram.Bot.Types;
using System.Threading;
using System.Threading.Tasks;

namespace Bot
{
    public static class UpdateRouter
    {
        public static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == Telegram.Bot.Types.Enums.UpdateType.CallbackQuery)
                await CallbackHandler.HandleCallbackAsync(bot, update, cancellationToken);
            else if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
                await MessageHandler.HandleMessageAsync(bot, update, cancellationToken);
        }
    }
}
