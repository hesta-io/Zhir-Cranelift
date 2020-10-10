using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.IO;
using System.Linq;

namespace WebDemo.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IWebHostEnvironment _env;
        private readonly StaticFileOptions _fileOptions;

        public IndexModel(IWebHostEnvironment env, IOptions<StaticFileOptions> fileOptions)
        {
            _env = env;
            _fileOptions = fileOptions.Value;
        }

        private string GetBaseUrl()
        {
            var request = this.Request;
            var host = request.Host.ToUriComponent();
            var pathBase = request.PathBase.ToUriComponent();

            return $"{request.Scheme}://{host}{pathBase}";
        }

        public string[] Samples { get; set; }

        public void OnGet()
        {
            var basePath = GetBaseUrl() + _fileOptions.RequestPath.ToUriComponent();
            Samples = Directory.EnumerateFiles(Path.Combine(_env.WebRootPath, "samples"))
                    .Select(f => Path.GetFileName(f))
                    .Select(f => basePath + new PathString("/samples/" + f).ToUriComponent())
                    .ToArray();
        }
    }
}
