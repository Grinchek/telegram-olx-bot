using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Data.Entities;

[Table("ConfirmedPayments")]
public class ConfirmedPayment
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    public string Code { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public DateTime RequestedAt { get; set; }
    public string? TransactionId { get; set; }

    public string PostId { get; set; } = string.Empty;

    [ForeignKey(nameof(PostId))]
    public PostData? Post { get; set; }

    public bool IsConfirmed { get; set; } = true;
}
