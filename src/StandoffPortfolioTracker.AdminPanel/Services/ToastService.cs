using System;
using System.Timers;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class ToastService : IDisposable
    {
        public event Action<string, ToastLevel>? OnShow;
        public event Action? OnHide;
        private System.Timers.Timer? _countdown;

        public void ShowToast(string message, ToastLevel level)
        {
            OnShow?.Invoke(message, level);
            StartCountdown();
        }

        private void StartCountdown()
        {
            SetCountdown();
            if (_countdown!.Enabled)
            {
                _countdown.Stop();
                _countdown.Start();
            }
            else
            {
                _countdown.Start();
            }
        }

        private void SetCountdown()
        {
            if (_countdown == null)
            {
                _countdown = new System.Timers.Timer(3000); // 3 секунды
                _countdown.Elapsed += HideToast;
                _countdown.AutoReset = false;
            }
        }

        private void HideToast(object? source, ElapsedEventArgs e)
        {
            OnHide?.Invoke();
        }

        public void Dispose()
        {
            _countdown?.Dispose();
        }
    }

    public enum ToastLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
}