
// /Models/PostData.cs
namespace Models;

public class PostData
{
    public string Title { get; set; }
    public string Price { get; set; }
    public string Description { get; set; }
    public string? ImageUrl { get; set; }
    public string SourceUrl { get; set; }

    // Нові поля
    public DateTime? PublishedAt { get; set; }
    public int? ChannelMessageId { get; set; }

    public PostData() { }

    public PostData(string title, string price, string description, string? imageUrl, string sourceUrl)
    {
        Title = title;
        Price = price;
        Description = description;
        ImageUrl = imageUrl;
        SourceUrl = sourceUrl;
    }
}



