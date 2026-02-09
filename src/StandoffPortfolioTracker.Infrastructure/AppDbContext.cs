using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; // 👈 1. ВАЖНО: Добавлено для Identity
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;

namespace StandoffPortfolioTracker.Infrastructure
{
    // 👇 2. ВАЖНО: Меняем наследование на IdentityDbContext<ApplicationUser>
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // === Таблицы Справочников ===
        public DbSet<GameCollection> GameCollections { get; set; }
        public DbSet<ItemBase> ItemBases { get; set; }
        public DbSet<MarketHistory> MarketHistory { get; set; }

        // === Таблицы Портфеля ===
        public DbSet<PortfolioAccount> PortfolioAccounts { get; set; }
        public DbSet<InventoryItem> InventoryItems { get; set; }
        public DbSet<AppliedAttachment> AppliedAttachments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 👇 3. ВАЖНО: Этот вызов обязателен для настройки таблиц Identity (Users, Roles и т.д.)
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<InventoryItem>()
                .Property(i => i.PurchasePrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<MarketHistory>()
                .Property(m => m.Price)
                .HasPrecision(18, 2);

            // Если удаляем Аккаунт -> удаляется весь Инвентарь
            modelBuilder.Entity<PortfolioAccount>()
                .HasMany(p => p.Items)
                .WithOne(i => i.PortfolioAccount)
                .HasForeignKey(i => i.PortfolioAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            // Если удаляем Предмет из инвентаря -> удаляются записи о его наклейках
            modelBuilder.Entity<InventoryItem>()
                .HasMany(i => i.Attachments)
                .WithOne(a => a.InventoryItem)
                .HasForeignKey(a => a.InventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}