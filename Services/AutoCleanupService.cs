using Telegram.Bot;
using Telegram.Bot.Types;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Services;
using Services.Interfaces;

public class AutoCleanupService
{
    private readonly IConfirmedPaymentsService _confirmedPaymentsService;
    private readonly IPostDraftService _postDraftSeevice;
    private readonly ITelegramBotClient _botClient;
    private readonly CancellationToken _cancellationToken;

    // 47 години поріг для авто-видалення
    private static readonly TimeSpan CleanupAge = TimeSpan.FromHours(47);

    public AutoCleanupService(
        IConfirmedPaymentsService confirmedPaymentsService,
        IPostDraftService postDraftService,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        _confirmedPaymentsService = confirmedPaymentsService;
        _postDraftSeevice = postDraftService;
        _botClient = botClient;
        _cancellationToken = cancellationToken;
    }

    public async Task RunAsync()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine("🧹 AutoCleanupService started");
                var allPosts = await _confirmedPaymentsService.GetAllAsync();
                var now = DateTime.UtcNow;

                foreach (var payment in allPosts)
                {
                    var post = payment.Post;
                    if (post == null || post.PublishedAt == null || post.ChannelMessageId == null)
                        continue;

                    var age = now - post.PublishedAt.Value;
                    Console.WriteLine($"Age:{age}\nPublished:{post.PublishedAt.Value}");

                    if (age >= CleanupAge)
                    {
                        try
                        {
                            // 1) Спочатку видаляємо повідомлення з каналу
                            var channel = Program.ChannelUsername; // без хардкоду
                            await _botClient.DeleteMessageAsync(
                                chatId: channel,
                                messageId: post.ChannelMessageId.Value,
                                cancellationToken: _cancellationToken);

                            // 2) Після успішного видалення — чистимо БД
                            await _confirmedPaymentsService.RemoveAsync(payment);

                            var removedByMsgId = await _postDraftSeevice.RemoveByChannelMessageIdAsync(post.ChannelMessageId.Value);
                            if (removedByMsgId == 0)
                            {
                                await _postDraftSeevice.RemoveByPostIdAsync(payment.PostId);
                            }

                            Console.WriteLine($"🧹 Видалено пост ID={payment.Id}, вік: {age.TotalHours:F1} год");
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

            // Частота перевірок: кожні 3 години 
            await Task.Delay(TimeSpan.FromHours(3), _cancellationToken);
        }
    }
}
