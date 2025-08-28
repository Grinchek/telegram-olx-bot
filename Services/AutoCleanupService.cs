using Telegram.Bot;
using Telegram.Bot.Types;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Services;
using Services.Interfaces;
using Telegram.Bot.Exceptions;

public class AutoCleanupService
{
    private readonly IConfirmedPaymentsService _confirmedPaymentsService;
    private readonly IPostDraftService _postDraftSeevice;
    private readonly ITelegramBotClient _botClient;
    private readonly CancellationToken _cancellationToken;

    // 47 годин поріг для авто-видалення
    private static readonly TimeSpan CleanupAge = TimeSpan.FromHours(45);

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
                    Console.WriteLine($"Age:{age}\nPublished:{post.PublishedAt.Value:O}\nMsgId:{post.ChannelMessageId}");

                    if (age >= CleanupAge)
                    {
                        try
                        {
                            // 1) Спочатку видаляємо повідомлення з каналу
                            var channel = new ChatId(Program.ChannelUsername);
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

                            Console.WriteLine($"🧹 Видалено пост ID={payment.Id}, вік: {age.TotalHours:F1} год, msgId={post.ChannelMessageId.Value}");
                        }
                        catch (ApiRequestException ex)
                        {
                            // Буває: >48 год, немає прав delete_messages, "message to delete not found" тощо.
                            Console.WriteLine($"⚠️ Cleanup: не вдалося видалити msgId={post.ChannelMessageId.Value}: {ex.Message}");
                            // ВАЖЛИВО: при помилці Telegram — не видаляємо рядки з БД.
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Cleanup unexpected error for msgId={post.ChannelMessageId.Value}: {ex}");
                            // БД також не чіпаємо
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ AutoCleanupService error: {ex}");
            }

            // Частота перевірок: кожні 1 годину
            await Task.Delay(TimeSpan.FromHours(1), _cancellationToken);
        }
    }
}
