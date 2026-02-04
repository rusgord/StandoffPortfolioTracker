using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StandoffPortfolioTracker.Core.Enums;

namespace StandoffPortfolioTracker.Core.Entities
{
    public class AppliedAttachment
    {
        public int Id { get; set; }

        public int InventoryItemId { get; set; } // На каком оружии висит
        public InventoryItem? InventoryItem { get; set; }

        public int StickerId { get; set; } // Ссылка на ItemBase (сама наклейка)
        public ItemBase? Sticker { get; set; }

        // 1-4 для наклеек. Можно использовать 5 для Брелока.
        public int SlotPosition { get; set; }
    }
}