using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using DotNetEnv;

namespace Data
{
    public class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
    {
        public BotDbContext CreateDbContext(string[] args)
        {
            // Завантажити змінні середовища
            Env.Load(); 

            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
            //Перевірка рядка підключення
            //Console.WriteLine("=== DEBUG: DB_CONNECTION_STRING ===");
            //Console.WriteLine(connectionString ?? "❌ НЕ знайдено змінну оточення");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("DB_CONNECTION_STRING not set in environment variables.");

            var optionsBuilder = new DbContextOptionsBuilder<BotDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new BotDbContext(optionsBuilder.Options);
        }
    }
}
