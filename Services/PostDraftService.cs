using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Services.Interfaces;
using System.Threading.Tasks;

namespace Services
{
    public class PostDraftService : IPostDraftService
    {
        private readonly BotDbContext _context;

        public PostDraftService(BotDbContext context)
        {
            _context = context;
        }
        public async Task SavePostAsync(PostData post)
        {
            _context.Posts.Attach(post);
            _context.Posts.Attach(post);
            _context.Entry(post).State = EntityState.Modified;

            await _context.SaveChangesAsync();
        }

        public async Task SaveDraftAsync(long chatId, PostData post)
        {
            post.ChatId = chatId;
            post.PublishedAt = null;
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();
        }

        public async Task<PostData?> GetDraftAsync(long chatId)
        {
            return await _context.Posts
                .Where(p => p.ChatId == chatId && p.PublishedAt == null)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();
        }


        public async Task DeleteDraftAsync(long chatId)
        {
            var draft = await GetDraftAsync(chatId);
            if (draft != null)
            {
                _context.Posts.Remove(draft);
                await _context.SaveChangesAsync();
            }
        }
        public async Task RemoveByChatIdAsync(long chatId)
        {
            var drafts = await _context.Posts
                .Where(p => p.ChatId == chatId)
                .ToListAsync();

            if (drafts.Count > 0)
            {
                _context.Posts.RemoveRange(drafts);
                await _context.SaveChangesAsync();
            }
        }
        public async Task<int> RemoveByChannelMessageIdAsync(int? channelMessageId)
        {
            return await _context.Posts
                .Where(p => p.ChannelMessageId == channelMessageId)
                .ExecuteDeleteAsync();
        }
        public async Task<int> RemoveByPostIdAsync(string postId)
        {
            return await _context.Posts
                .Where(p => p.Id == postId)
                .ExecuteDeleteAsync();
        }



    }
}
