// AutoCleanupService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Services.Interfaces;

namespace Services
{
    public class AutoCleanupService
    {
        private readonly IConfirmedPaymentsService _confirmedPaymentsService;
        private readonly IPostDraftService _postDraftService;
        private readonly ITelegramBotClient _botClient;
        private readonly CancellationToken _cancellationToken;

        // Для тестів: 1 хв. На проді вистав свій поріг (наприклад, 47 або 48 годин).
        private static readonly TimeSpan CleanupAge = TimeSpan.FromHours(45);

        public AutoCleanupService(
            IConfirmedPaymentsService confirmedPaymentsService,
            IPostDraftService postDraftService,
            ITelegramBotClient botClient,
            CancellationToken cancellationToken)
        {
            _confirmedPaymentsService = confirmedPaymentsService;
            _postDraftService = postDraftService;
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
                    var allPayments = await _confirmedPaymentsService.GetAllAsync();
                    var now = DateTime.UtcNow;

                    foreach (var payment in allPayments)
                    {
                        var post = payment.Post;
                        if (post == null || post.PublishedAt == null || post.ChannelMessageId == null)
                            continue;

                        var age = now - post.PublishedAt.Value;
                        Console.WriteLine($"Age:{age}\nPublished:{post.PublishedAt.Value:O}\nMsgId:{post.ChannelMessageId}");

                        if (age < CleanupAge)
                            continue;

                        var messageId = post.ChannelMessageId.Value;

                        // 1) Спочатку намагаємось видалити повідомлення з каналу
                        var tgDeleted = await TryDeleteFromChannelAsync(messageId, _cancellationToken);
                        if (!tgDeleted)
                        {
                            // Як у CallbackHandler: якщо TG видалити не вдалось — БД НЕ чіпаємо
                            continue;
                        }

                        // 2) Якщо Telegram успішно видалив — чистимо БД (платіж + драфти)
                        await _confirmedPaymentsService.RemoveAsync(payment);

                        try
                        {
                            var affected = await _postDraftService.RemoveByChannelMessageIdAsync(messageId);
                            if (affected == 0)
                            {
                                await _postDraftService.RemoveByPostIdAsync(payment.PostId);
                            }
                        }
                        catch (Exception ex2)
                        {
                            Console.WriteLine($"[AUTO] WARN drafts cleanup failed: {ex2.Message}");
                        }

                        Console.WriteLine($"🧹 Deleted msgId={messageId}; DB cleaned paymentId={payment.Id}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ AutoCleanupService error: {ex}");
                }

                // Частота перевірок 1 година(тут 1 хв для тестів)
                await Task.Delay(TimeSpan.FromHours(1), _cancellationToken);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Приватний хелпер: виконує лише TG-видалення. На помилці повертає false.
        // Базу даних тут не чіпаємо — це робиться вище після успіху TG.
        private async Task<bool> TryDeleteFromChannelAsync(int channelMessageId, CancellationToken ct)
        {
            try
            {
                var chatId = ResolveChannelChatId(); // підтримка -100… або @username
                await _botClient.DeleteMessageAsync(chatId, channelMessageId, ct);
                return true;
            }
            catch (ApiRequestException ex)
            {
                // Типові кейси: >48 год, немає прав delete_messages, "message to delete not found" тощо.
                Console.WriteLine($"⚠️ Cleanup: TG delete FAIL msgId={channelMessageId}: {ex.Message}");
                return false; // БД не чіпаємо
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Cleanup: unexpected TG err msgId={channelMessageId}: {ex}");
                return false; // БД не чіпаємо
            }
        }

        // Стабільно формуємо ChatId для каналу:
        // 1) Якщо є CHANNEL_ID (типу -100xxxxxxxxxx) у змінних середовища — беремо його.
        // 2) Інакше — падаємо на @username з Program.ChannelUsername (як у твоєму існуючому коді).
        private ChatId ResolveChannelChatId()
        {
            var channelIdEnv = Environment.GetEnvironmentVariable("CHANNEL_ID");
            if (long.TryParse(channelIdEnv, out var numeric))
                return new ChatId(numeric);

            // Fallback до username, якщо numeric id не заданий
            return new ChatId(Program.ChannelUsername);
        }
    }
}
