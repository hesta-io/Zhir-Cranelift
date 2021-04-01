
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cranelift.Pages
{
    [Authorize]
    public class JobsModel : PageModel
    {
        public void OnGet()
        {
          
        }
    }
}
