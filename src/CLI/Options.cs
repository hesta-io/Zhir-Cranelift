using System;
using CommandLine;

namespace CLI
{
    public class Options
    {
        [Option('i', "input", Required = true, HelpText = "path for the input file usually an image")]
        public string Input { get; set; }

        [Option('o', "output", Required = true, HelpText = "output path for recognized text")]
        public string Output { get; set; }

    }
}
