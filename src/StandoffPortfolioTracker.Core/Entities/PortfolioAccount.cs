using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        public List<InventoryItem> Items { get; set; } = new();
    }
}