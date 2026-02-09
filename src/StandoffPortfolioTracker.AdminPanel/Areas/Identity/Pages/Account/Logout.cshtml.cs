using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StandoffPortfolioTracker.Core.Entities;

namespace StandoffPortfolioTracker.AdminPanel.Areas.Identity.Pages.Account
{
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;

        public LogoutModel(SignInManager<ApplicationUser> signInManager)
        {
            _signInManager = signInManager;
        }

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            // 1. Разлогиниваем
            await _signInManager.SignOutAsync();

            // 2. СРАЗУ перекидываем на главную (или на страницу входа)
            if (returnUrl != null)
            {
                return LocalRedirect(returnUrl);
            }
            else
            {
                // Перекидываем на корень сайта
                return RedirectToPage("./Login");
            }
        }
    }
}