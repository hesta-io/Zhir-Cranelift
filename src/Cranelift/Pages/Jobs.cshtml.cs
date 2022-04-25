
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cranelift.Pages
{
    [Authorize]
    public class JobsModel : PageModel
    {
        public int? UserId { get; private set; }

        public void OnGet(int? userId)
        {
            UserId = userId;
        }
    }
}
