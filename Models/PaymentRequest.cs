using System;
using Models;

namespace Models;

public class PaymentRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8); // короткий унікальний id
    public string Code { get; set; }
    public long ChatId { get; set; }
    public PostData? Post { get; set; }
    public DateTime RequestedAt { get; set; }
    public string? TransactionId { get; set; }
}
