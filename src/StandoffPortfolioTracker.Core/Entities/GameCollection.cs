using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace StandoffPortfolioTracker.Core.Entities
{
    public class GameCollection
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        // Твое поле: Убрана ли коллекция из магазина/дропа
        public bool IsRemoved { get; set; }

        // Новое поле: Ссылка на иконку коллекции
        public string? ImageUrl { get; set; }

        public ICollection<ItemBase> Items { get; set; } = new List<ItemBase>();
    }
}