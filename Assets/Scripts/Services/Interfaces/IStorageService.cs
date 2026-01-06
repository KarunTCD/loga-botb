using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    public interface IStorageService : IService
    {
        // Basic operations
        void Save(string key, object value);
        T Load<T>(string key, T defaultValue = default);
        bool HasKey(string key);

        // Bulk operations
        void DeleteKey(string key);
        void DeleteKeysWithPrefix(string prefix);
        List<string> GetAllKeys();

        // Reset operation - delete all and reinitialize
        void ResetToDefaults();
    }
}