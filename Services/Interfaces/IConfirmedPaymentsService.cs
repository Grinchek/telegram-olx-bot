
using Data.Entities;

namespace Services.Interfaces;

public interface IConfirmedPaymentsService
{
    Task AddAsync(ConfirmedPayment request);
    Task<List<ConfirmedPayment>> GetAllAsync();
    Task<ConfirmedPayment?> GetByChannelMessageIdAsync(int messageId);
    Task RemoveAsync(ConfirmedPayment request);

}
