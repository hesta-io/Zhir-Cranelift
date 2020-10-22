
using Medallion.Shell;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Tesseract;

namespace WebDemo.Controllers
{
    public class UrlRequest
    {
        public string Url { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    [IgnoreAntiforgeryToken]
    public class OcrController : ControllerBase
    {
        private readonly ILogger<OcrController> _logger;
        private readonly IHttpClientFactory _clientFactory;

        public OcrController(
            ILogger<OcrController> logger,
            IHttpClientFactory clientFactory
            )
        {
            _logger = logger;
            _clientFactory = clientFactory;
        }

        [HttpPost("form")]
        public async Task<IActionResult> PostForm(
            [FromForm] IEnumerable<IFormFile> imageFiles)
        {
            var filePath = Path.GetTempFileName() + ".jpg";

            if (imageFiles.Any() == false)
                return BadRequest(new { error = "Please upload an image." });

            var first = imageFiles.First();

            using (var fileStream = System.IO.File.Create(filePath))
            {
                await first.CopyToAsync(fileStream);
            }

            return await ProcessImage(filePath);
        }

        [HttpPost("url")]
        public async Task<IActionResult> PostUrl(
            UrlRequest url)
        {
            var filePath = Path.GetTempFileName() + ".jpg";

            if (string.IsNullOrWhiteSpace(url.Url))
                return BadRequest(new { error = "Please send a valid URL." });

            var client = _clientFactory.CreateClient();
            var bytes = await client.GetByteArrayAsync(url.Url);

            await System.IO.File.WriteAllBytesAsync(filePath, bytes);

            return await ProcessImage(filePath);
        }

        private async Task<IActionResult> ProcessImage(string filePath)
        {
            try
            {
                var pix = Pix.LoadFromFile(filePath);
                pix.Colormap = null;

                var maxSide = 1000f;

                if (pix.Width > maxSide || pix.Height > maxSide)
                {
                    var wider = Math.Max(pix.Width, pix.Height);
                    var scale = 1f / (wider / maxSide);
                    pix = pix.Scale(scale, scale);
                }

                if (pix.Depth > 8)
                {
                    pix = pix.ConvertRGBToGray();
                }

                if (pix.Depth > 2)
                {
                    pix = pix.BinarizeSauvola(16, 0.25f, true);
                }

                pix = pix.Deskew();

                pix.Save(filePath);
                pix.Dispose();

                var preprocessed = await System.IO.File.ReadAllBytesAsync(filePath);

                var result = await RunTesseract(filePath);

                if (result.Success)
                {
                    return Ok(new
                    {
                        output = result.OutputOrError.Trim(),
                        preprocessed = ToBase64(preprocessed),
                    });
                }

                return BadRequest(new
                {
                    error = result.OutputOrError
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not scan image.");
                return BadRequest(new
                {
                    error = ex.Message
                });
            }
            finally
            {
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }
        }

        private string ToBase64(byte[] preprocessed)
        {
            return $"data:image/jpg;base64, {Convert.ToBase64String(preprocessed)}";
        }

        private class TesseractResult
        {
            public bool Success { get; set; }
            public string OutputOrError { get; set; }
        }

        private static async Task<TesseractResult> RunTesseract(string imageFile)
        {
            var modelsPath = Path.GetFullPath("models");

            var tempOutputFile = Path.GetTempFileName();

            var languages = new[] { "ckb", "eng" };

            try
            {
                var arguments = new[] { imageFile, tempOutputFile, "-l", string.Join("+", languages) };

                var command = Command.Run("tesseract", arguments, options =>
                {
                    options.EnvironmentVariables(new Dictionary<string, string>
                    {
                        { "TESSDATA_PREFIX", modelsPath }
                    });
                });

                await command.Task; // Wait for the process to exit

                tempOutputFile += ".txt"; // tesseract adds .txt at the end of the filename!
                if (command.Result.Success)
                {
                    var output = System.IO.File.ReadAllText(tempOutputFile);
                    return new TesseractResult
                    {
                        OutputOrError = output,
                        Success = true
                    };
                }
                else
                {
                    var lines = command.GetOutputAndErrorLines();
                    return new TesseractResult
                    {
                        OutputOrError = string.Join(Environment.NewLine, lines),
                        Success = false
                    };
                }
            }
            finally
            {
                System.IO.File.Delete(tempOutputFile);
            }
        }
    }
}
