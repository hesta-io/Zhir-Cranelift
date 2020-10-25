using CLI.Verbs;

using CommandLine;

using Medallion.Shell;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Worker;

namespace CLI
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var ocrPath = @"F:\ZhirAI\ocr-preprocess";

            //var poetryPath = System.Environment.GetEnvironmentVariable("PATH")
            //                    .Split(";")
            //                    .FirstOrDefault(i => i.Contains("poetry"));

            var command = Command.Run("poetry.bat", new[] { "install" }, options =>
            {
                options.WorkingDirectory(ocrPath);
            });

            await command.Task;

            Console.WriteLine(command.Result.StandardOutput);

            ////Parser.Default.ParseArguments<OCROptions>(args)
            ////   .WithParsed<OCROptions>(options => OCRVerb.Handle(options));
            //string[] lines = File.ReadAllLines("C:/Users/aram/Downloads/corpse-original.txt");
            //StringBuilder sb = new StringBuilder();
            //Random random = new Random();
            //int maxSentenceLengthInWords = 12;
            //int appendedWords = 0;
            //List<string> words = new List<string>();
            //string[] lineArray = null;
            //for (int i = 0; i < lines.Length; i += 1)
            //{
            //    lineArray = lines[i].Trim().Split(' ');
            //    words.AddRange(lineArray);
            //}
            //for (int i = 0; i < words.Count; i += 1)
            //{
            //    if (appendedWords < maxSentenceLengthInWords)
            //    {
            //        sb.Append(words[i] + " ");
            //        appendedWords += 1;
            //    }
            //    else
            //    {
            //        sb.Append(words[i] + " \n");
            //        appendedWords = 0;
            //        maxSentenceLengthInWords = random.Next(8, 15);
            //    }

            //}
            //File.WriteAllText("C:/Users/aram/Downloads/corpse-proccessed.txt", sb.ToString());
            //Console.WriteLine("done...");
        }
    }
}
