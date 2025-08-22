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
using System;

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

        Env.Load();

        if (AppDomain.CurrentDomain.FriendlyName.ToLower().Contains("ef"))
        {
            Console.WriteLine("🔧 EF Tools context detected. Skipping bot startup.");
            return;
        }

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

        services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));
        services.AddSingleton<IConfiguration>(config);

        // DbContext — Scoped
        services.AddDbContext<BotDbContext>(options =>
        {
            options.UseNpgsql(connectionString);

#if DEBUG
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
#endif
        });

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

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            db.Database.Migrate();
        }

        using (var scope = provider.CreateScope())
        {
            var scopedProvider = scope.ServiceProvider;

            PaymentService = scopedProvider.GetRequiredService<PaymentService>();
            PostDraftService = scopedProvider.GetRequiredService<IPostDraftService>();
            PostCounterService = scopedProvider.GetRequiredService<IPostCounterService>();
            PendingPaymentsService = scopedProvider.GetRequiredService<IPendingPaymentsService>();

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

            var botClient = scopedProvider.GetRequiredService<ITelegramBotClient>();
            var me = await botClient.GetMeAsync(cancellationToken);
            Console.WriteLine($"🤖 Bot @{me.Username} is running...");

            var updateRouter = scopedProvider.GetRequiredService<UpdateRouter>();

            botClient.StartReceiving(
                updateHandler: (bot, update, token) => updateRouter.HandleUpdateAsync(update),
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
        return string.IsNullOrEmpty(channel) ? channel : (channel.StartsWith("@") ? channel : "@" + channel);
    }
}
