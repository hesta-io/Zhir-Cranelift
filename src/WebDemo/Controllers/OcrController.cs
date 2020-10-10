
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

            return ProcessImage(filePath);
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

            return ProcessImage(filePath);
        }

        private IActionResult ProcessImage(string filePath)
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

                //var preprocessed = await System.IO.File.ReadAllBytesAsync(filePath);

                var result = RunTesseract(pix);

                if (result.Success)
                {
                    return Ok(new
                    {
                        output = result.OutputOrError.Trim(),
                        //preprocessed = ToBase64(preprocessed),
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

        private TesseractResult RunTesseract(Pix image)
        {
            var modelsPath = Path.GetFullPath("models");

            //try
            //{
            // BUG: For some reason, eng model is not used!
            using var engine = new TesseractEngine(modelsPath, "ckb+eng");
            var result = engine.Process(image);

            return new TesseractResult
            {
                Success = true,
                OutputOrError = result.GetText(),
            };
            //}
            //catch (Exception ex)
            //{
            //    return new TesseractResult
            //    {
            //        Success = false,
            //        OutputOrError = ex.Message
            //    };
            //}
        }
    }
}
