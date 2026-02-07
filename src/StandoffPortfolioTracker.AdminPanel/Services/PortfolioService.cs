using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class PortfolioService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public PortfolioService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        // === 1. Управление портфелями (ЭТО НОВОЕ) ===

        public async Task<List<PortfolioAccount>> GetAccountsAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.PortfolioAccounts.ToListAsync();
        }

        public async Task CreateAccountAsync(string name, string? description = null)
        {
            using var context = await _factory.CreateDbContextAsync();
            var newAccount = new PortfolioAccount
            {
                Name = name,
                Description = description
            };
            context.PortfolioAccounts.Add(newAccount);
            await context.SaveChangesAsync();
        }

        public async Task DeleteAccountAsync(int id)
        {
            using var context = await _factory.CreateDbContextAsync();
            var account = await context.PortfolioAccounts.FindAsync(id);
            if (account != null)
            {
                // Удаляем предметы этого портфеля (на всякий случай, если каскад не настроен)
                var items = context.InventoryItems.Where(i => i.PortfolioAccountId == id);
                context.InventoryItems.RemoveRange(items);

                context.PortfolioAccounts.Remove(account);
                await context.SaveChangesAsync();
            }
        }

        // === 2. Работа с инвентарем (ОБНОВЛЕНО ПОД ID) ===

        public async Task<List<InventoryItem>> GetInventoryAsync(int portfolioId)
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.InventoryItems
                .Include(i => i.ItemBase)
                .ThenInclude(ib => ib.Collection)
                .Where(i => i.PortfolioAccountId == portfolioId) // Фильтруем по ID портфеля
                .OrderByDescending(i => i.PurchaseDate)
                .ToListAsync();
        }

        public async Task AddPurchaseAsync(InventoryItem item)
        {
            using var context = await _factory.CreateDbContextAsync();
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();
        }

        public async Task DeleteItemAsync(int id)
        {
            using var context = await _factory.CreateDbContextAsync();
            var item = await context.InventoryItems.FindAsync(id);
            if (item != null)
            {
                context.InventoryItems.Remove(item);
                await context.SaveChangesAsync();
            }
        }
    }
}