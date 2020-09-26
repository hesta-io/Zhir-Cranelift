using CLI;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Worker;

namespace ClassLibraryTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            Dictionary<string, string> parsedArgs = CLIInterface.ParseArgs(args);
            Console.WriteLine("Loading image files ✓");

            foreach (string file in Directory
                .EnumerateFiles(parsedArgs["input"])
                .Where(file =>
                    file.ToLower().EndsWith("jpg") ||
                    file.ToLower().EndsWith("png"))
                .ToList())
            {
                Console.WriteLine("Processing " + Path.GetFileName(file) + " ...");

                PreProcess pr = new PreProcess(file);
                pr.Start();
                pr.GetProcessedImage().Save(file);
                string result = OCR.Run(file);
                File.WriteAllText(Path.GetDirectoryName(file) + "/" + Path.GetFileNameWithoutExtension(file) + ".txt", result);
            }
            Console.WriteLine("Processing image files finished ✓");
            /* ;
             
             
             */
        }



    }

}
