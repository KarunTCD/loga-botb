using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using System.Linq;

namespace LoGa.LudoEngine.Game
{
    public class InventoryManager : MonoBehaviour
    {
        public static InventoryManager Instance { get; private set; }

        private Inventory inventory;

        private IStorageService StorageService => ServiceLocator.GetService<IStorageService>();
        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LoadInventory();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void AddItem(InventoryItem item)
        {
            inventory.AddItem(item);
            SaveInventory();
            Debug.Log($"Added to inventory: {item.name} ({item.type}, ID: {item.itemId})");
        }

        public Inventory GetInventory()
        {
            return inventory;
        }

        public void PlayItemAudio(int itemId)
        {
            var item = inventory.items.FirstOrDefault(i => i.itemId == itemId);
            if (item != null && !item.audioClip.IsNull)
            {
                var instance = AudioService.CreateAudioInstance(item.audioClip);
                AudioService.PlayAudio(instance, Vector3.zero);
            }
        }

        public void MarkAsViewed(int itemId)
        {
            inventory.MarkItemAsViewed(itemId);
            SaveInventory();
        }

        private void SaveInventory()
        {
            StorageService.Save("PlayerInventory", inventory);
        }

        private void LoadInventory()
        {
            inventory = StorageService.Load<Inventory>("PlayerInventory") ?? new Inventory();
        }

        public void ResetInventory()
        {
            inventory = new Inventory();
            SaveInventory();
            Debug.Log("InventoryManager: Inventory reset to empty");
        }
    }
}