// /Storage/InMemoryRepository.cs
using System.Collections.Concurrent;
using Models;

namespace Storage
{
    public static class InMemoryRepository
    {
        public static ConcurrentDictionary<long, PostData> PendingPosts { get; } = new();
    }
}
