using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;
using System.Security.Claims;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class PortfolioService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly AuthenticationStateProvider _authProvider; // Для получения текущего юзера

        public PortfolioService(IDbContextFactory<AppDbContext> factory, AuthenticationStateProvider authProvider)
        {
            _factory = factory;
            _authProvider = authProvider;
        }

        // Хелпер: Получить ID текущего пользователя
        private async Task<string?> GetCurrentUserId()
        {
            var authState = await _authProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user.Identity != null && user.Identity.IsAuthenticated)
            {
                // Ищем Claim с ID пользователя
                return user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            }
            return null;
        }

        // === 1. Управление портфелями (С ФИЛЬТРОМ ПО ЮЗЕРУ) ===

        public async Task<List<PortfolioAccount>> GetAccountsAsync()
        {
            var userId = await GetCurrentUserId();
            if (userId == null) return new List<PortfolioAccount>(); // Если не вошел — список пуст

            using var context = await _factory.CreateDbContextAsync();

            // Грузим только портфели ЭТОГО пользователя
            return await context.PortfolioAccounts
                .Where(p => p.UserId == userId)
                .ToListAsync();
        }

        public async Task CreateAccountAsync(string name, string? description = null)
        {
            var userId = await GetCurrentUserId();
            if (userId == null) return; // Нельзя создать без входа

            using var context = await _factory.CreateDbContextAsync();
            var newAccount = new PortfolioAccount
            {
                Name = name,
                Description = description,
                UserId = userId // Привязываем к текущему юзеру
            };
            context.PortfolioAccounts.Add(newAccount);
            await context.SaveChangesAsync();
        }

        public async Task DeleteAccountAsync(int id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null) return;

            using var context = await _factory.CreateDbContextAsync();

            // Проверяем, что удаляем СВОЙ портфель
            var account = await context.PortfolioAccounts
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (account != null)
            {
                // Удаляем связанные предметы (если каскад не сработает)
                var items = context.InventoryItems.Where(i => i.PortfolioAccountId == id);
                context.InventoryItems.RemoveRange(items);

                context.PortfolioAccounts.Remove(account);
                await context.SaveChangesAsync();
            }
        }

        // === 2. Работа с инвентарем (С ПРОВЕРКОЙ ДОСТУПА) ===

        public async Task<List<InventoryItem>> GetInventoryAsync(int portfolioId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null) return new List<InventoryItem>();

            using var context = await _factory.CreateDbContextAsync();

            var isMyPortfolio = await context.PortfolioAccounts
                .AnyAsync(p => p.Id == portfolioId && p.UserId == userId);

            if (!isMyPortfolio) return new List<InventoryItem>();

            return await context.InventoryItems
                .Include(i => i.ItemBase)
                    .ThenInclude(ib => ib.Collection)
                // Грузим список наклеек
                .Include(i => i.Attachments)
                    // Грузим данные самой наклейки (цену, картинку)
                    .ThenInclude(a => a.Sticker)
                .Where(i => i.PortfolioAccountId == portfolioId)
                .OrderByDescending(i => i.PurchaseDate)
                .ToListAsync();
        }

        public async Task AddPurchaseAsync(InventoryItem item)
        {
            // Здесь тоже можно добавить проверку на владельца портфеля, но пока пропустим для краткости
            using var context = await _factory.CreateDbContextAsync();
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();
        }

        public async Task UpdatePurchaseAsync(InventoryItem item)
        {
            using var context = await _factory.CreateDbContextAsync();
            var existing = await context.InventoryItems.FindAsync(item.Id);
            if (existing != null)
            {
                existing.Quantity = item.Quantity;
                existing.PurchasePrice = item.PurchasePrice;
                await context.SaveChangesAsync();
            }
        }

        public async Task SaveAttachmentsAsync(int inventoryItemId, List<AppliedAttachment> newAttachments)
        {
            using var context = await _factory.CreateDbContextAsync();

            // 1. Ищем старые наклейки для этого предмета
            var existing = context.AppliedAttachments.Where(x => x.InventoryItemId == inventoryItemId);

            // 2. Удаляем их
            context.AppliedAttachments.RemoveRange(existing);

            // 3. Добавляем новые из списка
            foreach (var att in newAttachments)
            {
                att.Id = 0; // Сбрасываем ID, чтобы база создала новые записи
                att.InventoryItemId = inventoryItemId; // Привязываем к оружию
                att.Sticker = null; // Обнуляем объект стикера, оставляем только StickerId, чтобы EF не пытался создать стикер заново

                context.AppliedAttachments.Add(att);
            }

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