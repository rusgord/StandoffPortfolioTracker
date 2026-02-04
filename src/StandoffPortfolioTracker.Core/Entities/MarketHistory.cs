using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StandoffPortfolioTracker.Core.Entities
{
    public class MarketHistory
    {
        public int Id { get; set; }

        public int ItemBaseId { get; set; }
        public ItemBase? ItemBase { get; set; }

        public decimal Price { get; set; } // Цена на момент записи
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow; // Когда обновили цену
    }
}
