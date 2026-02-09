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
        private readonly AuthenticationStateProvider _authProvider;

        public PortfolioService(IDbContextFactory<AppDbContext> factory, AuthenticationStateProvider authProvider)
        {
            _factory = factory;
            _authProvider = authProvider;
        }

        // Helper: Get Current User ID (Only works in standard SignalR scope)
        private async Task<string?> GetCurrentUserId()
        {
            try
            {
                var authState = await _authProvider.GetAuthenticationStateAsync();
                var user = authState.User;
                if (user.Identity != null && user.Identity.IsAuthenticated)
                {
                    return user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                }
            }
            catch
            {
                // If called from a background Scope, this might fail. Return null.
                return null;
            }
            return null;
        }

        // =========================================================
        // 1. METHODS FOR "MY PORTFOLIO" PAGE (Secure, checks Auth)
        // =========================================================

        public async Task<List<PortfolioAccount>> GetMyAccountsAsync()
        {
            var userId = await GetCurrentUserId();
            if (userId == null) return new List<PortfolioAccount>();

            using var context = await _factory.CreateDbContextAsync();
            return await context.PortfolioAccounts
                .Where(p => p.UserId == userId)
                .ToListAsync();
        }

        public async Task<List<InventoryItem>> GetMyInventoryAsync(int portfolioId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null) return new List<InventoryItem>();

            using var context = await _factory.CreateDbContextAsync();

            // Verify ownership
            var isOwner = await context.PortfolioAccounts
                .AnyAsync(p => p.Id == portfolioId && p.UserId == userId);

            if (!isOwner) return new List<InventoryItem>();

            return await context.InventoryItems
                .Include(i => i.ItemBase).ThenInclude(ib => ib.Collection)
                .Include(i => i.Attachments).ThenInclude(a => a.Sticker)
                .Where(i => i.PortfolioAccountId == portfolioId)
                .OrderByDescending(i => i.PurchaseDate)
                .ToListAsync();
        }

        // =========================================================
        // 2. METHODS FOR "USER PROFILE" PAGE (Public/Read-Only)
        //    These DO NOT check AuthState. Logic is handled by the caller.
        // =========================================================

        public async Task<List<PortfolioAccount>> GetPortfoliosByUserAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return new List<PortfolioAccount>();

            using var context = await _factory.CreateDbContextAsync();

            return await context.PortfolioAccounts
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToListAsync();
        }

        public async Task<List<InventoryItem>> GetInventoryReadOnlyAsync(int portfolioId)
        {
            using var context = await _factory.CreateDbContextAsync();

            return await context.InventoryItems
                .Include(i => i.ItemBase)
                .AsNoTracking() // Faster for stats
                .Where(i => i.PortfolioAccountId == portfolioId)
                .ToListAsync();
        }

        // This method you were using in UserProfile needs to be "dumb"
        // I renamed it above to GetInventoryReadOnlyAsync to be clear, 
        // but to keep compatibility with your UserProfile code, I'll alias it:
        public async Task<List<InventoryItem>> GetInventoryAsync(int portfolioId)
        {
            // Just forward to the simple method
            return await GetInventoryReadOnlyAsync(portfolioId);
        }

        // =========================================================
        // 3. WRITES (Create/Update/Delete) - Always need Auth
        // =========================================================

        public async Task CreateAccountAsync(string name, string? description = null)
        {
            var userId = await GetCurrentUserId();
            if (userId == null) return;

            using var context = await _factory.CreateDbContextAsync();
            context.PortfolioAccounts.Add(new PortfolioAccount
            {
                Name = name,
                Description = description,
                UserId = userId
            });
            await context.SaveChangesAsync();
        }

        public async Task DeleteAccountAsync(int id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null) return;

            using var context = await _factory.CreateDbContextAsync();
            var account = await context.PortfolioAccounts
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (account != null)
            {
                var items = context.InventoryItems.Where(i => i.PortfolioAccountId == id);
                context.InventoryItems.RemoveRange(items);
                context.PortfolioAccounts.Remove(account);
                await context.SaveChangesAsync();
            }
        }

        public async Task AddPurchaseAsync(InventoryItem item)
        {
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
            var existing = context.AppliedAttachments.Where(x => x.InventoryItemId == inventoryItemId);
            context.AppliedAttachments.RemoveRange(existing);

            foreach (var att in newAttachments)
            {
                att.Id = 0;
                att.InventoryItemId = inventoryItemId;
                att.Sticker = null;
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