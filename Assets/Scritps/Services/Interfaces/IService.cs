using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    public interface IService
    {
        bool IsInitialized { get; }
        Task<bool> InitializeAsync();
    }
}