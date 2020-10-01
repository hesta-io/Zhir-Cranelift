using CLI.Verbs;

using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Worker;

namespace CLI
{
    class Program
    {
        static void Main(string[] args)
        {
            //Parser.Default.ParseArguments<OCROptions>(args)
            //   .WithParsed<OCROptions>(options => OCRVerb.Handle(options));
            string[] lines = File.ReadAllLines("C:/Users/aram/Downloads/corpse-original.txt");
            StringBuilder sb = new StringBuilder();
            Random random = new Random();
            int maxSentenceLengthInWords = 12;
            int appendedWords = 0;
            List<string> words = new List<string>();
            string[] lineArray = null;
            for (int i = 0; i < lines.Length; i += 1)
            {
                lineArray = lines[i].Trim().Split(' ');
                words.AddRange(lineArray);
            }
            for (int i = 0; i < words.Count; i += 1)
            {
                if (appendedWords < maxSentenceLengthInWords)
                {
                    sb.Append(words[i] + " ");
                    appendedWords += 1;
                }
                else
                {
                    sb.Append(words[i] + " \n");
                    appendedWords = 0;
                    maxSentenceLengthInWords = random.Next(8, 15);
                }

            }
            File.WriteAllText("C:/Users/aram/Downloads/corpse-proccessed.txt", sb.ToString());
            Console.WriteLine("done...");
        }
    }
}
