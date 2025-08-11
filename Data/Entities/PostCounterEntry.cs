using System.ComponentModel.DataAnnotations;

namespace Data.Entities;

public class PostCounterEntry
{
    [Key]
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}
