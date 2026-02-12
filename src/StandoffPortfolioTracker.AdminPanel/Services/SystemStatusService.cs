using System;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class SystemStatusService
    {
        public DateTime? LastPriceUpdate { get; set; }
        public DateTime? LastBoostCheck { get; set; }
        public string LastError { get; set; } = "Нет ошибок";
        public bool IsProcessing { get; set; }

        public void LogError(string error)
        {
            LastError = $"{DateTime.Now:HH:mm:ss}: {error}";
        }
    }
}