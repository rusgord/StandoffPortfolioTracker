using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using StandoffPortfolioTracker.Core.Enums;

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
        // 🎮 Игровые данные
        [MaxLength(20)]
        public string? StandoffGameId { get; set; }

        // 🎨 Кастомизация
        public string? ProfileFrame { get; set; } = "default"; // Рамка (default, gold, neon, fire)
        public string? FavoriteSkinsJson { get; set; } // JSON список ID любимых скинов

        // 🔒 Приватность
        public bool IsGameIdPublic { get; set; } = true;
        public bool IsBlogEnabled { get; set; } = true; // Блог/Стена комментариев

        // === Настройки приватности ===
        public bool IsProfilePublic { get; set; } = true;
        public bool ShowPortfolioValue { get; set; } = false;
        public bool ShowGrowth { get; set; } = false;
        public bool ShowTopItems { get; set; } = false;

        public decimal Balance { get; set; } = 0;

        // ПОДПИСКА PRO
        public DateTime? ProExpirationDate { get; set; }

        public SubscriptionType SubType { get; set; } = SubscriptionType.None;

        public bool IsAutoRenew { get; set; } = true;

        // Хелпер: Проверка, активна ли подписка прямо сейчас
        public bool IsPro => ProExpirationDate.HasValue && ProExpirationDate.Value > DateTime.UtcNow;
    }
}