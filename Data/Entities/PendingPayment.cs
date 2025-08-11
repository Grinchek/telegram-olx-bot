using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Data.Entities;

[Table("PendingPayments")]
public class PendingPayment
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    public string Code { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public DateTime RequestedAt { get; set; }
    public string? TransactionId { get; set; }

    [ForeignKey(nameof(Post))]
    public string PostId { get; set; } = default!;

    public PostData Post { get; set; } = default!;

    public bool IsConfirmed { get; set; } = false;
}
