using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace StandoffPortfolioTracker.Core.Entities
{
    public class ApplicationUser : IdentityUser
    {
        // Дата регистрации
        public DateTime JoinDate { get; set; } = DateTime.UtcNow;

        // Ссылка на аватарку (храним путь к файлу)
        public string? AvatarUrl { get; set; }

        // Цель портфолио (сколько голды хочет накопить)
        public decimal PortfolioGoal { get; set; }

        public string? DisplayName { get; set; }

        // === Настройки приватности ===
        public bool IsProfilePublic { get; set; } = true;
        public bool ShowPortfolioValue { get; set; } = false;
        public bool ShowGrowth { get; set; } = false;
        public bool ShowTopItems { get; set; } = false;

        public decimal Balance { get; set; } = 0;

        // ПОДПИСКА PRO
        public DateTime? ProExpirationDate { get; set; }

        // Хелпер: Проверка, активна ли подписка прямо сейчас
        public bool IsPro => ProExpirationDate.HasValue && ProExpirationDate.Value > DateTime.UtcNow;
    }
}