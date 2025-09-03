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
        public string id;
        public string name;
        public string description;
        public ItemType type;
        public EventReference audioClip;
        public string sourceTimeLayer;
        public string sourcePOI;
        public bool isNew;
    }

    public enum ItemType
    {
        Character,
        Artifact,
        Music
    }

    [System.Serializable]
    public class Inventory
    {
        public List<InventoryItem> items = new List<InventoryItem>();

        public void AddItem(InventoryItem item)
        {
            if (HasItem(item.id)) return;

            item.isNew = true;
            items.Add(item);
        }

        public bool HasItem(string itemId)
        {
            return items.Any(i => i.id == itemId);
        }

        public List<InventoryItem> GetItemsByType(ItemType type)
        {
            return items.Where(i => i.type == type).ToList();
        }

        public void MarkItemAsViewed(string itemId)
        {
            var item = items.FirstOrDefault(i => i.id == itemId);
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