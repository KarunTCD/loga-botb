using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FMODUnity;

namespace LoGa.LudoEngine.Game
{
    [System.Serializable]
    public class InventoryItem
    {
        public int itemId;              // Character ID or Reward ID
        public string name;
        public string description;
        public ItemType type;           // Character or Artifact
        public string audioClip;
        public string sourceTimeLayer;
        public int sourceCharacterId;   // Which character gave this item
        public bool isNew;
    }

    public enum ItemType
    {
        Character,  // Character audio/story
        Artifact    // Reward items
    }

    [System.Serializable]
    public class Inventory
    {
        public List<InventoryItem> items = new List<InventoryItem>();

        public void AddItem(InventoryItem item)
        {
            if (HasItem(item.itemId)) return;
            item.isNew = true;
            items.Add(item);
        }

        public bool HasItem(int itemId)
        {
            return items.Any(i => i.itemId == itemId);
        }

        public List<InventoryItem> GetItemsByType(ItemType type)
        {
            return items.Where(i => i.type == type).ToList();
        }

        public void MarkItemAsViewed(int itemId)
        {
            var item = items.FirstOrDefault(i => i.itemId == itemId);
            if (item != null)
            {
                item.isNew = false;
            }
        }

        public int GetNewItemCount()
        {
            return items.Count(i => i.isNew);
        }
    }
}