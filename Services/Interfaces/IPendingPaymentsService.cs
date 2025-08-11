
using System.Threading.Tasks;
using Data.Entities;

namespace Services.Interfaces;

public interface IPendingPaymentsService
{
    Task AddAsync(PendingPayment request);
    Task<List<PendingPayment>> GetAllAsync();
    Task<PendingPayment?> GetByCodeAsync(string code);
    Task RemoveAsync(PendingPayment request);
    Task RemoveAllByChatIdAsync(long chatId);
    Task<PendingPayment?> GetByChatIdAsync(long chatId);
    Task<PendingPayment?> GetLastByChatIdAsync(long chatId);
    Task RemoveOlderThanAsync(TimeSpan maxAge);

}
