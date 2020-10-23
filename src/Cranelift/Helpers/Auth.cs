using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Cranelift.Helpers
{
    public class CustomCookieAuthenticationEvents : CookieAuthenticationEvents
    {
        public const string LastChangedKey = "LastChanged";

        private static readonly DateTime _startDate = DateTime.UtcNow;

        public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
        {
            var userPrincipal = context.Principal;

            var lastChanged = userPrincipal.Claims.Where(c => c.Type == LastChangedKey)
                                                  .Select(c => c.Value)
                                                  .FirstOrDefault();

            if (string.IsNullOrEmpty(lastChanged) ||
                DateTime.TryParse(lastChanged, out var lastChangedDate) == false ||
                lastChangedDate < _startDate)
            {
                context.RejectPrincipal();

                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }
    }

    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            return httpContext.User.Identity.IsAuthenticated &&
                   httpContext.User.IsInRole("Administrator");
        }
    }
}
