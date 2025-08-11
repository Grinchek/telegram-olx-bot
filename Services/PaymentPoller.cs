using Telegram.Bot;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Services;
using Services.Interfaces;
using Data.Entities;
using System.Net.WebSockets;

public class PaymentPoller
{
    private readonly PaymentService _paymentService;
    private readonly ITelegramBotClient _botClient;
    private readonly CancellationToken _cancellationToken;
    private readonly IPostDraftService _postDraftService;
    private readonly PostPublisher _postPublisher;
    private readonly IPostCounterService _postCounterService;

    public PaymentPoller(
        PaymentService paymentService,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        IPostDraftService postDraftService,
        PostPublisher postPublisher,
        IPostCounterService postCounterService)
    {
        _paymentService = paymentService;
        _botClient = botClient;
        _cancellationToken = cancellationToken;
        _postDraftService = postDraftService;
        _postPublisher = postPublisher;
        _postCounterService = postCounterService;
    }

    public async Task RunAsync()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                var newPayments = await _paymentService.GetNewPaymentsAsync();

                foreach (var payment in newPayments)
                {
                    var post = payment.Post;
                    var tryIncrement = await _postCounterService.TryIncrementAsync();
                    if (post == null||!tryIncrement)
                        continue;

                    // Публікація в канал
                    var messageId = await _postPublisher.PublishPostAsync(post, post.ChatId, true, _cancellationToken);
                    var chatId = post.ChatId;
                    post.PublishedAt = DateTime.UtcNow;
                    post.ChannelMessageId = messageId;

                    await _paymentService.ConfirmPaymentAsync(payment);

                    Console.WriteLine($"✅ Оплата підтверджена — оголошення опубліковано (ChatId: {post.ChatId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ PaymentPoller error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMinutes(2), _cancellationToken);
        }
    }
}
