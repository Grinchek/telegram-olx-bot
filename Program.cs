using Telegram.Bot;
using Bot;
using Services;
using Storage;
using DotNetEnv;
using System;

Env.Load(); // автоматично зчитує .env


var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN")!;
var monobankToken = Environment.GetEnvironmentVariable("MONOBANK_TOKEN")!;

var botClient = new TelegramBotClient(botToken);
var cts = new CancellationTokenSource();
var cancellationToken = cts.Token;

// Створення необхідних директорій
Directory.CreateDirectory("data");

// Ініціалізація сервісів
Program.PaymentService = new PaymentService(monobankToken);
var paymentPoller = new PaymentPoller(Program.PaymentService, botClient, cancellationToken);

// Завантаження очікувань та підтверджених оплат
PendingPayments.Load();
ConfirmedPayments.Load();

Console.WriteLine($"🤖 Bot {await botClient.GetMeAsync()} is running...");

// Запуск фонової перевірки оплат
_ = Task.Run(() => paymentPoller.RunAsync(), cancellationToken);

// Обробка оновлень
botClient.StartReceiving(
    updateHandler: async (client, update, token) =>
    {
        if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
            await MessageHandler.HandleMessageAsync(client, update, token);
        else if (update.Type == Telegram.Bot.Types.Enums.UpdateType.CallbackQuery)
            await CallbackHandler.HandleCallbackAsync(client, update, token);
    },
    pollingErrorHandler: async (client, exception, token) =>
    {
        Console.WriteLine($"❌ Telegram API Error: {exception.Message}");
    },
    cancellationToken: cancellationToken
);

// Зупинка по Ctrl+C
Console.CancelKeyPress += (sender, args) =>
{
    args.Cancel = true;
    cts.Cancel();
};

await Task.Delay(-1, cancellationToken);

// Доступ до PaymentService з інших класів
public partial class Program
{
    public static string BotUsername = "@BARACHOLKA_UA_bot";
    public static string ChannelUsername = "@baraholka_market_ua";
    public static long AdminChatId = 6764916214;
    public static PaymentService PaymentService { get; set; } = default!;
}
