using CommandLine;
using CommandLine.Text;

using System.Collections.Generic;

namespace CLI
{
    [Verb("ocr", HelpText = "Runs tesseract on one or more images.")]
    public class OCROptions
    {
        [Value(0, HelpText = "Input image file or a folder that contains images.")]
        public string Input { get; set; }

        [Value(1, HelpText = "Output folder to put the results.")]
        public string Output { get; set; }

        [Option('l', "languages", HelpText = "Language models to use. The language names must be separated by space. By default it uses only kurdish models. Pass in `all` to use all language models. Example: -l ckb ara")]
        public IEnumerable<string> Languages { get; set; }

        [Usage()]
        public static IEnumerable<Example> Examples
        {
            get
            {
                return new List<Example>() {
                    new Example("Run OCR on one image:",
                            new OCROptions { Input = "image1.jpg", Output = "output" }),

                    new Example("Run OCR on all images in a folder:",
                            new OCROptions { Input = "images", Output = "output" }),

                    new Example("Run OCR on all images in a folder using all available models.",
                            new OCROptions { Input = "images", Output = "output", Languages = new [] { "all" } }),

                    new Example("Run OCR on all images in a folder using ckb and ara models.",
                            new OCROptions { Input = "images", Output = "output", Languages = new [] { "ckb", "ara" } }),
                };
            }
        }
    }
}
