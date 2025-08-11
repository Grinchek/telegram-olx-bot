
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class ConfirmedPaymentsService : Interfaces.IConfirmedPaymentsService
{
    private readonly Data.BotDbContext _context;

    public ConfirmedPaymentsService(Data.BotDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ConfirmedPayment request)
    {
        _context.ConfirmedPayments.Add(request);
        await _context.SaveChangesAsync();
        Console.WriteLine("✅ ConfirmedPayment збережено.");
    }

    public async Task<List<ConfirmedPayment>> GetAllAsync()
    {
        return await _context.ConfirmedPayments.Include(p => p.Post).ToListAsync();
    }
    public async Task<ConfirmedPayment?> GetByChannelMessageIdAsync(int messageId)
    {
        return await _context.ConfirmedPayments
            .AsNoTracking()
            .Where(p => p.Post.ChannelMessageId == messageId)
            .Select(p => new ConfirmedPayment
            {
                Id = p.Id,
                ChatId = p.ChatId,
                PostId = p.PostId
            })
            .FirstOrDefaultAsync();
    }


    public async Task RemoveAsync(ConfirmedPayment request)
    {
        if (request == null) return;

        var id = request.Id;

        // 1) Якщо в Local уже є трекнута сутність — видаляємо її
        var tracked = _context.ConfirmedPayments.Local.FirstOrDefault(x => x.Id == id);
        if (tracked != null)
        {
            _context.ConfirmedPayments.Remove(tracked);
        }
        else
        {
            // 2) Інакше не чіпаємо навігації, видаляємо "стабом" по ключу
            var stub = new ConfirmedPayment { Id = id };
            _context.Entry(stub).State = EntityState.Deleted;
            // Альтернатива: _context.ConfirmedPayments.Attach(stub); _context.ConfirmedPayments.Remove(stub);
        }

        await _context.SaveChangesAsync();
    }



}
