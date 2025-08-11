using Telegram.Bot;
using Telegram.Bot.Types;
using System.Threading;
using System.Threading.Tasks;
using Services.Interfaces;

namespace Bot
{
    public class UpdateRouter
    {
        private readonly CallbackHandler _callbackHandler;
        private readonly MessageHandler _messageHandler;
        private readonly CancellationToken _cancellationToken;
        private readonly IConfirmedPaymentsService _confirmedPaymentsService;
        private readonly ITelegramBotClient _botClient;

        public UpdateRouter(
            ITelegramBotClient botClient,
            CallbackHandler callbackHandler,
            MessageHandler messageHandler,
            IConfirmedPaymentsService confirmedPaymentsService,
            CancellationToken cancellationToken)
        {
            _botClient = botClient;
            _callbackHandler = callbackHandler;
            _messageHandler = messageHandler;
            _confirmedPaymentsService = confirmedPaymentsService;
            _cancellationToken = cancellationToken;
        }

        public async Task HandleUpdateAsync(Update update)
        {
            if (update.Type == Telegram.Bot.Types.Enums.UpdateType.CallbackQuery)
                await _callbackHandler.HandleCallbackAsync(_botClient, update, _cancellationToken);
            else if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
                await _messageHandler.HandleMessageAsync(_botClient, update, _confirmedPaymentsService, _cancellationToken);
        }
    }
}
