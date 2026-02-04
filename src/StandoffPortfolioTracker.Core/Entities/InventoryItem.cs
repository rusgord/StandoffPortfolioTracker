using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StandoffPortfolioTracker.Core.Entities
{
    public class InventoryItem
    {
        public int Id { get; set; }

        public int PortfolioAccountId { get; set; }
        public PortfolioAccount? PortfolioAccount { get; set; }

        public int ItemBaseId { get; set; } // Ссылка на справочник (какой это скин)
        public ItemBase? ItemBase { get; set; }

        public bool IsStatTrack { get; set; } // StatTrack или нет

        public decimal PurchasePrice { get; set; } // За сколько купил (за штуку)
        public int Quantity { get; set; } = 1; // Кол-во

        public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;

        // Если на скине есть наклейки и/или брелки
        public List<AppliedAttachment> Attachments { get; set; } = new();
    }
}