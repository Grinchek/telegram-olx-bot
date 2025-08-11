using System;
using System.Threading;
using System.Threading.Tasks;
using Services.Interfaces;

public class PendingCleanupService
{
    private readonly IPendingPaymentsService _pendingPaymentsService;

    public PendingCleanupService(IPendingPaymentsService pendingPaymentsService)
    {
        _pendingPaymentsService = pendingPaymentsService;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            try
            {
                Console.WriteLine($"🧹 Cleaning up old pending payments at {DateTime.UtcNow}...");
                await _pendingPaymentsService.RemoveOlderThanAsync(TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Auto-cleanup failed: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMinutes(30));
        }
    }
}
