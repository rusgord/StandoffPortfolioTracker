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
        public string Name { get; set; } = string.Empty; // "Origin", "Assistance" и т.д.

        public bool IsRemoved { get; set; } // Убрана ли из магазина
    }
}
