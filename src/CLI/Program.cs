﻿using CLI.Verbs;

using CommandLine;
using Worker;

namespace CLI
{
    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<OCROptions>(args)
               .WithParsed<OCROptions>(options => OCRVerb.Handle(options));
        }
    }
}
