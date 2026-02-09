using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StandoffPortfolioTracker.Core.Entities;

namespace StandoffPortfolioTracker.AdminPanel.Areas.Identity.Pages.Account.Manage
{
    public class ExternalLoginsModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public ExternalLoginsModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public IList<UserLoginInfo> CurrentLogins { get; set; }
        public IList<AuthenticationScheme> OtherLogins { get; set; }
        public bool ShowRemoveButton { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            // Загружаем текущие привязки (например, Google, VK)
            CurrentLogins = await _userManager.GetLoginsAsync(user);

            // Загружаем доступные, но еще не привязанные
            OtherLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync())
                .Where(auth => CurrentLogins.All(ul => auth.Name != ul.LoginProvider))
                .ToList();

            // Кнопку "Удалить" показываем, только если есть пароль ИЛИ больше 1 способа входа
            ShowRemoveButton = user.PasswordHash != null || CurrentLogins.Count > 1;

            return Page();
        }

        // Удаление привязки
        public async Task<IActionResult> OnPostRemoveLoginAsync(string loginProvider, string providerKey)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var result = await _userManager.RemoveLoginAsync(user, loginProvider, providerKey);
            if (!result.Succeeded)
            {
                StatusMessage = "The external login was not removed.";
                return RedirectToPage();
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Привязка успешно удалена.";
            return RedirectToPage();
        }

        // Запрос на добавление новой привязки
        public async Task<IActionResult> OnPostLinkLoginAsync(string provider)
        {
            // Очищаем существующий куки внешней аутентификации, чтобы гарантировать чистый логин
            await _signInManager.SignOutAsync();

            // RedirectUrl указывает на LinkLoginCallback
            var redirectUrl = Url.Page("./ExternalLogins", pageHandler: "LinkLoginCallback");
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, _userManager.GetUserId(User));

            return new ChallengeResult(provider, properties);
        }

        // Коллбэк после успешного входа через соцсеть
        public async Task<IActionResult> OnGetLinkLoginCallbackAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var info = await _signInManager.GetExternalLoginInfoAsync(userId);

            if (info == null)
            {
                // Если что-то пошло не так, пробуем восстановить пользователя
                // В некоторых сценариях GetExternalLoginInfoAsync может вернуть null, если куки потерялись
                StatusMessage = "Ошибка загрузки информации от внешнего провайдера.";
                return RedirectToPage();
            }

            var result = await _userManager.AddLoginAsync(user, info);
            if (!result.Succeeded)
            {
                StatusMessage = "Ошибка: Этот аккаунт уже привязан к другому пользователю.";
                return RedirectToPage();
            }

            // Очищаем куки внешнего логина
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            StatusMessage = "Аккаунт успешно привязан!";
            return RedirectToPage();
        }
    }
}