using Data.Entities;
using System.Threading.Tasks;

namespace Services.Interfaces
{
    public interface IPostDraftService
    {
        Task SavePostAsync(PostData post);
        Task SaveDraftAsync(long chatId, PostData post);
        Task<PostData?> GetDraftAsync(long chatId);
        Task DeleteDraftAsync(long chatId);
        Task RemoveByChatIdAsync(long chatId);
        Task<int> RemoveByChannelMessageIdAsync(int? channelMessageId);
        Task<int> RemoveByPostIdAsync(string postId);
    }
}
