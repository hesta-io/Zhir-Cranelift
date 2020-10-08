using Medallion.Shell;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Worker;

namespace WebDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OcrController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Post(
            [FromForm] IEnumerable<IFormFile> imageFiles)
        {
            var filePath = Path.GetTempFileName();

            try
            {
                if (imageFiles.Any() == false)
                    return BadRequest(new { error = "Please upload an image." });

                var first = imageFiles.First();

                using (var fileStream = System.IO.File.Create(filePath))
                {
                    await first.CopyToAsync(fileStream);
                }

                //using var process = new PreProcess(filePath);

                //process.Start();

                //var bitmap = process.GetProcessedImage();

                //using var stream = new MemoryStream();
                //bitmap.Save(stream, ImageFormat.Jpeg);
                //var preprocessed = stream.ToArray();
                //bitmap.Save(filePath);

                var result = await RunTesseract(filePath);

                if (result.Success)
                {
                    return Ok(new
                    {
                        output = result.OutputOrError.Trim(),
                       // preprocessed = ToBase64(preprocessed),
                    });
                }

                return BadRequest(new
                {
                    error = result.OutputOrError
                });
            }
            catch (Exception ex)
            {
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
            return $"data:image/jppg;base64, {Convert.ToBase64String(preprocessed)}";
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

            var languages = new[] { "ckb", "ara" };

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
