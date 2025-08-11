using System.Threading.Tasks;

public interface IPostCounterService
{
    Task<bool> TryIncrementAsync();
    Task<int> GetCurrentCountAsync();
    Task DecrementAsync();
}
