using System;

namespace StandoffPortfolioTracker.Core.Entities
{
    public class WalletTransaction
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.UtcNow;

        public string? ExternalSystem { get; set; } // Например: "DonationAlerts"
        public string? ExternalTransactionId { get; set; } // Уникальный ID доната (чтобы не было дублей)
    }
}