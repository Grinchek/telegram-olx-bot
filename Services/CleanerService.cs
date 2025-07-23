using Models;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Storage;
using System.Net.Http;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace Services;

public class CleanerService
{
    private readonly ITelegramBotClient _bot;
    private readonly CancellationToken _token;

    private static readonly string ChannelUsername = Environment.GetEnvironmentVariable("CHANNEL_USERNAME") ?? "@baraholka_market_ua";


    public CleanerService(ITelegramBotClient bot, CancellationToken token)
    {
        _bot = bot;
        _token = token;
    }

    public async Task RunAsync()
    {
        while (!_token.IsCancellationRequested)
        {
            try
            {
                ConfirmedPayments.Load();
                var posts = ConfirmedPayments.GetAll();
                var now = DateTime.UtcNow;
                var toRemove = new List<PaymentRequest>();

                foreach (var post in posts)
                {
                    if (post.Post == null) continue;

                    var publishedAt = post.Post.PublishedAt;
                    var expired = publishedAt.HasValue && (now - publishedAt.Value).TotalHours > 72;

                    var isActive = await CheckIfOlxActive(post.Post.SourceUrl!);

                    if (expired || !isActive)
                    {
                        toRemove.Add(post);
                        try
                        {
                            if (post.Post.ChannelMessageId.HasValue)
                            {
                                await _bot.DeleteMessageAsync(
                                    chatId: ChannelUsername,
                                    messageId: post.Post.ChannelMessageId.Value,
                                    cancellationToken: _token
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Помилка при видаленні: {ex.Message}");
                        }
                    }
                }

                foreach (var item in toRemove)
                    posts.Remove(item);

                ConfirmedPayments.Save();

                Console.WriteLine($"🧹 Очищено {toRemove.Count} старих або неактивних оголошень.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CleanerService error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromHours(1), _token);
        }
    }

    private static async Task<bool> CheckIfOlxActive(string sourceUrl)
    {
        try
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(sourceUrl);
            var html = await response.Content.ReadAsStringAsync();

            return !html.Contains("Оголошення неактивне") &&
                   !html.Contains("Nie znaleziono ogłoszenia") &&
                   response.StatusCode == System.Net.HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }
}
