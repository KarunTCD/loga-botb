using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    /// <summary>
    /// Base interface for all services in the game
    /// </summary>
    public interface IService
    {
        /// <summary>
        /// Check if service is initialized
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Initialize the service asynchronously
        /// </summary>
        Task<bool> InitializeAsync();

        /// <summary>
        /// Reset the service to allow re-initialization after failure
        /// Called when user retries after service initialization failure
        /// </summary>
        void Reset();
    }
}