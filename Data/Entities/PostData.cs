// File: Data/Entities/PostData.cs

namespace Data.Entities;

public class PostData
{
    public string Id { get; set; } = Guid.NewGuid().ToString(); // ��������� �������������
    public long ChatId { get; set; } // ID �����������, �� ������� ����
    public string Title { get; set; } = string.Empty;
    public string Price { get; set; } = "";
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; } // Nullable, �� ���� ���� ���� �� �� ������������
    public int? ChannelMessageId { get; set; } // ID ����������� � �����
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

}
