using Cranelift.Helpers;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Cranelift.Pages
{
    public class LoginModel : PageModel
    {
        public LoginModel(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        [BindProperty]
        public string Username { get; set; }

        [BindProperty]
        public string Password { get; set; }

        [BindProperty]
        public bool RememberMe { get; set; }

        public string Message { get; set; }

        public IConfiguration Configuration { get; }

        public IActionResult OnGet()
        {
            if (User.Identity.IsAuthenticated)
            {
                return Redirect("/");
            }

            return Page();
        }

        public async Task OnPost(string redirectUrl)
        {
            var users = Configuration.GetSection("Users").GetChildren()
                    .Select(u => new
                    {
                        Username = u["Username"],
                        Password = u["Password"],
                    }).ToArray();

            var user = users.FirstOrDefault(u => u.Username == Username && u.Password == Password);
            if (user is null)
            {
                Message = "Invalid Username/Password.";
                return;
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, "Administrator"),
                new Claim(CustomCookieAuthenticationEvents.LastChangedKey, DateTime.UtcNow.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new AuthenticationProperties
            {
                RedirectUri = redirectUrl ?? Url.Page("/"),
                IsPersistent = RememberMe,
                ExpiresUtc = DateTime.UtcNow.AddDays(7), // Expires in 7 days
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
        }
    }
}
