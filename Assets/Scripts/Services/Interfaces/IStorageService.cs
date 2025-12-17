using System;
using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    public interface IStorageService : IService
    {
        void Save(string key, object value);
        T Load<T>(string key);
        bool HasKey(string key);
        void DeleteKey(string key);
        void DeleteAll();
        void ResetToDefault(string key);
        void ResetAllToDefaults();
    }
}