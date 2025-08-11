using Telegram.Bot;
using Bot;
using Services;
using Services.Interfaces;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Data;
using Microsoft.Extensions.DependencyInjection;

public partial class Program
{
    public static string BotUsername = Environment.GetEnvironmentVariable("BOT_USERNAME")!;
    public static string ChannelUsername = Environment.GetEnvironmentVariable("CHANNEL_USERNAME")!;
    public static long AdminChatId = long.Parse(Environment.GetEnvironmentVariable("ADMIN_CHAT_ID")!);

    public static PaymentService PaymentService { get; set; } = default!;
    public static IPostDraftService PostDraftService { get; set; } = default!;
    public static IPostCounterService PostCounterService { get; set; } = default!;
    public static IPendingPaymentsService PendingPaymentsService { get; set; } = default!;

    public static async Task Main(string[] args)
    {
        if (args.Contains("ef")) return;

        Env.Load();

        if (AppDomain.CurrentDomain.FriendlyName.ToLower().Contains("ef"))
        {
            Console.WriteLine("🔧 EF Tools context detected. Skipping bot startup.");
            return;
        }

        var cancellationToken = new CancellationTokenSource().Token;

        var services = new ServiceCollection();

        var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN")!;
        var monobankToken = Environment.GetEnvironmentVariable("MONOBANK_TOKEN")!;
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")!;

        // TelegramBotClient — можна залишити Singleton
        services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));

        // DbContext — Scoped
        services.AddDbContext<BotDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Scoped сервіси
        services.AddScoped<IPendingPaymentsService, PendingPaymentsService>();
        services.AddScoped<IConfirmedPaymentsService, ConfirmedPaymentsService>();
        services.AddScoped<IPostDraftService, PostDraftService>();
        services.AddScoped<IPostCounterService, PostCounterService>();

        // PaymentService — теж Scoped
        services.AddScoped<PaymentService>(provider =>
        {
            var pending = provider.GetRequiredService<IPendingPaymentsService>();
            var confirmed = provider.GetRequiredService<IConfirmedPaymentsService>();
            var context = provider.GetRequiredService<BotDbContext>();
            return new PaymentService( context,monobankToken, pending, confirmed);
        });

        // Інші Scoped класи
        services.AddScoped<CallbackHandler>();
        services.AddScoped<MessageHandler>();
        services.AddScoped<PostPublisher>();
        services.AddScoped<PendingCleanupService>();
        services.AddScoped<UpdateRouter>(provider =>
        {
            var bot = provider.GetRequiredService<ITelegramBotClient>();
            var callback = provider.GetRequiredService<CallbackHandler>();
            var message = provider.GetRequiredService<MessageHandler>();
            var confirmed = provider.GetRequiredService<IConfirmedPaymentsService>();
            return new UpdateRouter(bot, callback, message, confirmed, cancellationToken);
        });

        services.AddScoped<PaymentPoller>(provider =>
        {
            var paymentService = provider.GetRequiredService<PaymentService>();
            var bot = provider.GetRequiredService<ITelegramBotClient>();
            var draft = provider.GetRequiredService<IPostDraftService>();
            var postPublisher = provider.GetRequiredService<PostPublisher>();
            var postCounter = provider.GetRequiredService<IPostCounterService>();
            return new PaymentPoller(paymentService, bot, cancellationToken, draft, postPublisher,postCounter);
        });

        services.AddScoped<AutoCleanupService>();

        var provider = services.BuildServiceProvider();

        // Міграція БД
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            db.Database.Migrate();
        }

        // Глобальні сервіси
        using (var scope = provider.CreateScope())
        {
            var scopedProvider = scope.ServiceProvider;

            PaymentService = scopedProvider.GetRequiredService<PaymentService>();
            PostDraftService = scopedProvider.GetRequiredService<IPostDraftService>();
            PostCounterService = scopedProvider.GetRequiredService<IPostCounterService>();
            PendingPaymentsService = scopedProvider.GetRequiredService<IPendingPaymentsService>();

            // Фонові служби з використанням scoped-інстансів

            // Перевірка платежів
            _ = Task.Run(async () =>
            {
                using var s = provider.CreateScope();
                var poller = s.ServiceProvider.GetRequiredService<PaymentPoller>();
                await poller.RunAsync();
            }, cancellationToken);

            // Автоочищення неоплачених публікацій(інтервал - 1 година)
            _ = Task.Run(async () =>
            {
                using var s = provider.CreateScope();
                var cleanup = s.ServiceProvider.GetRequiredService<PendingCleanupService>();
                await cleanup.RunAsync();
            }, cancellationToken);



            // Запуск бота
            var botClient = scopedProvider.GetRequiredService<ITelegramBotClient>();
            Console.WriteLine($"🤖 Bot {await botClient.GetMeAsync()} is running...");

            var updateRouter = scopedProvider.GetRequiredService<UpdateRouter>();

            botClient.StartReceiving(
                updateHandler: (bot, update, token) =>
                    updateRouter.HandleUpdateAsync(update),
                pollingErrorHandler: async (client, exception, token) =>
                {
                    Console.WriteLine($"❌ Telegram API Error: {exception.Message}");
                },
                cancellationToken: cancellationToken
            );

            Console.CancelKeyPress += (sender, args) =>
            {
                args.Cancel = true;
                Environment.Exit(0);
            };

            await Task.Delay(-1, cancellationToken);
        }
    }
}
