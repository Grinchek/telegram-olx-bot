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
    updateHandler: UpdateRouter.HandleUpdateAsync,
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
    public static string BotUsername = Environment.GetEnvironmentVariable("BOT_USERNAME")!;
    public static string ChannelUsername = Environment.GetEnvironmentVariable("CHANNEL_USERNAME")!;
    public static long AdminChatId = long.Parse(Environment.GetEnvironmentVariable("ADMIN_CHAT_ID")!);

    public static PaymentService PaymentService { get; set; } = default!;
}

