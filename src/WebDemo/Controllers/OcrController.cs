using Medallion.Shell;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Tesseract;

namespace WebDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [IgnoreAntiforgeryToken]
    public class OcrController : ControllerBase
    {
        private readonly ILogger<OcrController> _logger;

        public OcrController(ILogger<OcrController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Post(
            [FromForm] IEnumerable<IFormFile> imageFiles)
        {
            var filePath = Path.GetTempFileName() + ".jpg";

            try
            {
                if (imageFiles.Any() == false)
                    return BadRequest(new { error = "Please upload an image." });

                var first = imageFiles.First();

                using (var fileStream = System.IO.File.Create(filePath))
                {
                    await first.CopyToAsync(fileStream);
                }

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
                return BadRequest(new { error = ex.Message });
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
            using var engine = new TesseractEngine(modelsPath, "ckb");
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
