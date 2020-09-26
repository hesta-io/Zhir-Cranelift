using Medallion.Shell;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Worker;

namespace CLI.Verbs
{
    public static class OCRVerb
    {
        public static int Handle(OCROptions options)
        {
            List<string> inputFiles;
            if (File.Exists(options.Input))
            {
                inputFiles = new List<string> { options.Input };
            }
            else if (Directory.Exists(options.Input))
            {
                inputFiles = Directory.EnumerateFiles(options.Input)
                                     .Where(file => file.IsImage())
                                     .ToList();
            }
            else
            {
                Console.WriteLine("Input path does not exist!");
                return 1;
            }

            if (!Directory.Exists(options.Output))
            {
                Directory.CreateDirectory(options.Output);
            }

            Console.WriteLine($"Running OCR on {inputFiles.Count:N0} images...");

            Parallel.ForEach(inputFiles, file =>
            {
                using (var pr = new PreProcess(file))
                {
                    pr.Start();
                    pr.GetProcessedImage().Save(file);
                }

                var result = RunTesseract("tesseract", file, options.Languages?.ToArray());

                if (!result.Success)
                {
                    Console.WriteLine($"Error:\n{result.OutputOrError}");
                }

                var fileName = Path.GetFileNameWithoutExtension(file);
                var outputPath = Path.Combine(options.Output, $"{fileName}.txt");
                File.WriteAllText(outputPath, result.OutputOrError);

                Console.WriteLine($"Processed {file}.");
            });

            Console.WriteLine("Processing image files finished ✓");
            return 0;
        }

        private class TesseractResult
        {
            public bool Success { get; set; }
            public string OutputOrError { get; set; }
        }

        private static TesseractResult RunTesseract(string tesseractPath, string imageFile, params string[] languages)
        {
            tesseractPath = Path.GetFullPath(tesseractPath);

            var tempOutputFile = Path.GetTempFileName();
            var modelsPath = Path.Combine(tesseractPath, "tessdata");

            if (languages is null || languages.Length == 0)
            {
                languages = Directory.EnumerateFiles(modelsPath, "*.traineddata")
                                  .Select(f => Path.GetFileNameWithoutExtension(f))
                                  .Where(f => f.ToLowerInvariant().StartsWith("ckb"))
                                  .ToArray();
            }
            else if (languages[0].ToLowerInvariant() == "all")
            {
                languages = Directory.EnumerateFiles(modelsPath, "*.traineddata")
                                  .Select(f => Path.GetFileNameWithoutExtension(f))
                                  .ToArray();
            }

            try
            {
                var arguments = new[] { imageFile, tempOutputFile, "-l", string.Join("+", languages) };

                var command = Command.Run("tesseract.exe", arguments, options =>
                {
                    options.WorkingDirectory(tesseractPath);
                    options.EnvironmentVariables(new Dictionary<string, string>
                    {
                        { "TESSDATA_PREFIX", Path.Combine(tesseractPath, "tessdata") }
                    });
                });

                command.Wait(); // Wait for the process to exit

                tempOutputFile += ".txt"; // tesseract adds .txt at the end of the filename!
                if (command.Result.Success)
                {
                    var output = File.ReadAllText(tempOutputFile);
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
                File.Delete(tempOutputFile);
            }
        }

        private static bool IsImage(this string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" ||
                   extension == ".png";
        }
    }
}
