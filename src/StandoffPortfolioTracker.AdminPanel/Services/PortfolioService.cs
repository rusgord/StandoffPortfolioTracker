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

        // Получить весь инвентарь для конкретного аккаунта
        public async Task<List<InventoryItem>> GetInventoryAsync(int accountId)
        {
            using var context = _factory.CreateDbContext();
            return await context.InventoryItems
                .Where(i => i.PortfolioAccountId == accountId)
                .Include(i => i.ItemBase) // Подтягиваем название скина
                .OrderByDescending(i => i.PurchaseDate)
                .ToListAsync();
        }

        // Добавить покупку
        public async Task AddPurchaseAsync(InventoryItem item)
        {
            using var context = _factory.CreateDbContext();
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();
        }

        // Удалить (если ошибся)
        public async Task DeleteItemAsync(int id)
        {
            using var context = _factory.CreateDbContext();
            var item = await context.InventoryItems.FindAsync(id);
            if (item != null)
            {
                context.InventoryItems.Remove(item);
                await context.SaveChangesAsync();
            }
        }
    }
}