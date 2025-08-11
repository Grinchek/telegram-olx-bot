
using Telegram.Bot;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Services;
using Services.Interfaces;

public class AutoCleanupService
{
    private readonly IConfirmedPaymentsService _confirmedPaymentsService;
    private readonly ITelegramBotClient _botClient;
    private readonly CancellationToken _cancellationToken;

    public AutoCleanupService(
        IConfirmedPaymentsService confirmedPaymentsService,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        _confirmedPaymentsService = confirmedPaymentsService;
        _botClient = botClient;
        _cancellationToken = cancellationToken;
    }

    public async Task RunAsync()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                var allPosts = await _confirmedPaymentsService.GetAllAsync();
                var now = DateTime.UtcNow;

                foreach (var payment in allPosts)
                {
                    var post = payment.Post;
                    if (post == null || post.PublishedAt == null || post.ChannelMessageId == null)
                        continue;

                    var age = now - post.PublishedAt.Value;
                    if (age.TotalDays >= 3)
                    {
                        try
                        {
                            await _botClient.DeleteMessageAsync(
                                chatId: "@baraholka_market_ua",
                                messageId: post.ChannelMessageId.Value,
                                cancellationToken: _cancellationToken);

                            await _confirmedPaymentsService.RemoveAsync(payment);

                            Console.WriteLine($"🧹 Видалено пост ID={payment.Id}, вік: {age.TotalDays:F1} днів");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Помилка при видаленні поста ID={payment.Id}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ AutoCleanupService error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromHours(3), _cancellationToken); // повтор кожні 3 год
        }
    }
}
