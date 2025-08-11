
using System.Threading.Tasks;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class PendingPaymentsService : Interfaces.IPendingPaymentsService
{
    private readonly Data.BotDbContext _context;

    public PendingPaymentsService(Data.BotDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(PendingPayment request)
    {
        // ⛔ Перевірка на дублікати
        var existing = await _context.PendingPayments
            .FirstOrDefaultAsync(p => p.PostId == request.PostId);

        if (existing != null)
        {
            Console.WriteLine($"⚠️ PendingPayment already exists for PostId: {request.PostId}");
            return;
        }

        await _context.PendingPayments.AddAsync(request);
        await _context.SaveChangesAsync();
    }


    public async Task<List<PendingPayment>> GetAllAsync()
    {
        return await _context.PendingPayments.Include(p => p.Post).ToListAsync();
    }

    public async Task<PendingPayment?> GetByCodeAsync(string code)
    {
        return await _context.PendingPayments.Include(p => p.Post)
            .FirstOrDefaultAsync(p => p.Code == code);
    }

    public async Task RemoveAsync(PendingPayment request)
    {
        _context.PendingPayments.Remove(request);
        await _context.SaveChangesAsync();
    }
    public async Task RemoveAllByChatIdAsync(long chatId)
    {
        var toRemove = await _context.PendingPayments
            .Where(p => p.ChatId == chatId)
            .ToListAsync();

        if (toRemove.Any())
        {
            _context.PendingPayments.RemoveRange(toRemove);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<PendingPayment?> GetByChatIdAsync(long chatId)
    {
        return await _context.PendingPayments
            .Include(p => p.Post)
            .FirstOrDefaultAsync(p => p.ChatId == chatId);
    }
    public async Task<PendingPayment?> GetLastByChatIdAsync(long chatId)
    {
        return await _context.PendingPayments
            .Where(p => p.ChatId == chatId)
            .OrderByDescending(p => p.RequestedAt)
            .FirstOrDefaultAsync();
    }
    public async Task RemoveOlderThanAsync(TimeSpan maxAge)
    {
        var threshold = DateTime.UtcNow - maxAge;

        var oldPending = await _context.PendingPayments
            .Where(p => p.RequestedAt < threshold)
            .ToListAsync();

        var oldDrafts = await _context.Posts
            .Where(p => p.PublishedAt == null && p.CreatedAt < threshold)
            .ToListAsync();

        if (oldPending.Count == 0 && oldDrafts.Count == 0) return;

        _context.PendingPayments.RemoveRange(oldPending);
        _context.Posts.RemoveRange(oldDrafts);

        await _context.SaveChangesAsync();

        Console.WriteLine($"🧹 Cleanup: pending={oldPending.Count}, drafts={oldDrafts.Count}");
    }




}
