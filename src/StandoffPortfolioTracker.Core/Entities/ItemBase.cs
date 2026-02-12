using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using StandoffPortfolioTracker.Core.Enums;

namespace StandoffPortfolioTracker.Core.Entities
{
    // Базовое описание предмета (не конкретный скин в инвентаре, а вид предмета в целом)
    public class ItemBase
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty; // Например "AKR" или "Empire Case"

        public string? SkinName { get; set; } // Например "Necromancer". У кейсов может быть null.

        public ItemRarity Rarity { get; set; }
        public ItemType Type { get; set; }
        public ItemKind Kind { get; set; }

        public int CollectionId { get; set; }
        public GameCollection? Collection { get; set; } // Навигационное свойство

        public string? ImageUrl { get; set; } // Ссылка на картинку (потом пригодится для UI)
        public decimal CurrentMarketPrice { get; set; }
        // Добавляем это поле, чтобы хранить "кривое" имя с сайта (с кавычками)
        public string? OriginalName { get; set; }

        // Новое поле: является ли скин StatTrack версией
        public bool IsStatTrack { get; set; }
        // Является ли скин паттерновым
        public bool IsPattern { get; set; }

        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    }
}
