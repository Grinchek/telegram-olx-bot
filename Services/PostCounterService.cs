using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;


public class PostCounterService : IPostCounterService
{
    private readonly BotDbContext _dbContext;

    public PostCounterService(BotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryIncrementAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = await _dbContext.PostCounters.FindAsync(today);

        if (entry == null)
        {
            entry = new PostCounterEntry { Date = today, Count = 1 };
            _dbContext.PostCounters.Add(entry);
        }
        else if (entry.Count >= 100)
        {
            return false;
        }
        else
        {
            entry.Count++;
            _dbContext.PostCounters.Update(entry);
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<int> GetCurrentCountAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = await _dbContext.PostCounters.FindAsync(today);
        return entry?.Count ?? 0;
    }
    public async Task DecrementAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = await _dbContext.PostCounters.FindAsync(today);
        if (entry != null && entry.Count > 0)
        {
            entry.Count--;
            _dbContext.PostCounters.Update(entry);
            await _dbContext.SaveChangesAsync();
        }
    }

}
