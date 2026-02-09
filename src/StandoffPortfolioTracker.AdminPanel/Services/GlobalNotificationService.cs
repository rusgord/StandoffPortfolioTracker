using System;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class GlobalNotificationService
    {
        public event Action<string, string, ToastLevel>? OnNotificationReceived;

        public void NotifyUser(string targetUserId, string message, ToastLevel level)
        {
            // Пишем в консоль сервера
            Console.WriteLine($"[GLOBAL RADIO] 📢 Отправка сообщения для ID: {targetUserId}");

            if (OnNotificationReceived == null)
            {
                Console.WriteLine("[GLOBAL RADIO] ⚠️ Никто не подписан на события!");
            }
            else
            {
                // Показываем, сколько слушателей (вкладок) сейчас подключено
                Console.WriteLine($"[GLOBAL RADIO] ✅ Слушателей в эфире: {OnNotificationReceived.GetInvocationList().Length}");
                OnNotificationReceived.Invoke(targetUserId, message, level);
            }
        }
    }
}