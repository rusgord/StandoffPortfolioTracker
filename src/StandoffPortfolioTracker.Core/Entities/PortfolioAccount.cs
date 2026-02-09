using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace StandoffPortfolioTracker.Core.Entities
{
    public class PortfolioAccount
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty; // "Основа", "Трейд-акк" или же позже реализовать подключение гугл аккаунта

        public string? Description { get; set; } // Как следует из названия переменной - возможность добавлять описание. Либо это будет краткая подпись при наведении на профиль, либо же какая-либо навигация в профиле или т.п.

        // Чей это портфель?
        public string UserId { get; set; } = string.Empty;

        public List<InventoryItem> Items { get; set; } = new();
    }
}