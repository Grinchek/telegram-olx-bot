using Telegram.Bot;
using Bot;
using Services;
using Services.Interfaces;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public partial class Program
{
    public static string BotUsername { get; private set; } = string.Empty;
    public static string ChannelUsername { get; private set; } = string.Empty;
    public static long AdminChatId { get; private set; }

    public static PaymentService PaymentService { get; set; } = default!;
    public static IPostDraftService PostDraftService { get; set; } = default!;
    public static IPostCounterService PostCounterService { get; set; } = default!;
    public static IPendingPaymentsService PendingPaymentsService { get; set; } = default!;

    public static async Task Main(string[] args)
    {
        if (args.Contains("ef")) return;

        // Локально підхопимо .env, на проді працює EnvironmentFile systemd
        try { Env.Load(); } catch { }

        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var botToken = Require(config, "BOT_TOKEN");
        var monobankToken = Require(config, "MONOBANK_TOKEN");
        var connectionString = Require(config, "DB_CONNECTION_STRING");

        BotUsername = config["BOT_USERNAME"] ?? string.Empty;
        ChannelUsername = NormalizeChannel(Require(config, "CHANNEL_USERNAME"));

        var adminRaw = Require(config, "ADMIN_CHAT_ID");
        if (!long.TryParse(adminRaw, out var adminId))
            throw new InvalidOperationException("ADMIN_CHAT_ID має бути числом (long).");
        AdminChatId = adminId;

        var cancellationToken = new CancellationTokenSource().Token;
        var services = new ServiceCollection();

        // Логування
        services.AddLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddSimpleConsole(o =>
            {
                o.IncludeScopes = true;
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            lb.SetMinimumLevel(LogLevel.Information);
            lb.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Information);
            lb.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
        });

        // TelegramBotClient — Singleton
        services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));
        // IConfiguration — Singleton
        services.AddSingleton<IConfiguration>(config);

        // DbContext — Scoped
        services.AddDbContext<BotDbContext>(options =>
        {
            options
                .UseNpgsql(connectionString)
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors()
                .LogTo(Console.WriteLine,
                    new[]
                    {
                        DbLoggerCategory.Database.Command.Name,
                        DbLoggerCategory.Update.Name
                    },
                    LogLevel.Information);
        });

        // Scoped сервіси
        services.AddScoped<IPendingPaymentsService, PendingPaymentsService>();
        services.AddScoped<IConfirmedPaymentsService, ConfirmedPaymentsService>();
        services.AddScoped<IPostDraftService, PostDraftService>();
        services.AddScoped<IPostCounterService, PostCounterService>();

        services.AddScoped<PaymentService>(provider =>
        {
            var pending = provider.GetRequiredService<IPendingPaymentsService>();
            var confirmed = provider.GetRequiredService<IConfirmedPaymentsService>();
            var context = provider.GetRequiredService<BotDbContext>();
            var cfg = provider.GetRequiredService<IConfiguration>();
            return new PaymentService(context, cfg, pending, confirmed);
        });

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
            return new PaymentPoller(paymentService, bot, cancellationToken, draft, postPublisher, postCounter);
        });

        services.AddScoped<AutoCleanupService>(provider =>
        {
            var confirmed = provider.GetRequiredService<IConfirmedPaymentsService>();
            var draft = provider.GetRequiredService<IPostDraftService>();
            var bot = provider.GetRequiredService<ITelegramBotClient>();
            return new AutoCleanupService(confirmed, draft, bot, cancellationToken);
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();

        // Міграція
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            logger.LogInformation("Applying migrations...");
            db.Database.Migrate();
            logger.LogInformation("Migrations applied.");
        }

        // Глобальні сервіси і запуск бота
        using (var scope = provider.CreateScope())
        {
            var scopedProvider = scope.ServiceProvider;

            PaymentService = scopedProvider.GetRequiredService<PaymentService>();
            PostDraftService = scopedProvider.GetRequiredService<IPostDraftService>();
            PostCounterService = scopedProvider.GetRequiredService<IPostCounterService>();
            PendingPaymentsService = scopedProvider.GetRequiredService<IPendingPaymentsService>();

            // Фонові завдання
            _ = Task.Run(async () =>
            {
                using var s = provider.CreateScope();
                var poller = s.ServiceProvider.GetRequiredService<PaymentPoller>();
                await poller.RunAsync();
            }, cancellationToken);

            _ = Task.Run(async () =>
            {
                using var s = provider.CreateScope();
                var cleanup = s.ServiceProvider.GetRequiredService<PendingCleanupService>();
                await cleanup.RunAsync();
            }, cancellationToken);

            _ = Task.Run(async () =>
            {
                using var s = provider.CreateScope();
                var cleanOld = s.ServiceProvider.GetRequiredService<AutoCleanupService>();
                await cleanOld.RunAsync();
            }, cancellationToken);

            // Запуск бота
            var botClient = scopedProvider.GetRequiredService<ITelegramBotClient>();
            var me = await botClient.GetMeAsync(cancellationToken);
            Console.WriteLine($"🤖 Bot @{me.Username} is running...");

            var updateRouter = scopedProvider.GetRequiredService<UpdateRouter>();

            botClient.StartReceiving(
                updateHandler: (bot, update, token) => updateRouter.HandleUpdateAsync(update),
                pollingErrorHandler: async (client, exception, token) =>
                {
                    Console.WriteLine($"❌ Telegram API Error: {exception.GetType().Name}: {exception.Message}");
                    if (exception.InnerException != null)
                        Console.WriteLine($"   Inner: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
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

    private static string Require(IConfiguration config, string key)
    {
        var v = config[key];
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException($"ENV змінна '{key}' не встановлена.");
        return v;
    }

    private static string NormalizeChannel(string channel)
    {
        channel = channel.Trim();
        if (string.IsNullOrEmpty(channel)) return channel;
        return channel.StartsWith("@") ? channel : "@" + channel;
    }
}
