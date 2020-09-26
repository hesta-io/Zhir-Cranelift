using System;
using CommandLine;

namespace CLI
{
    public class Options
    {
        [Option('i', "input", Required = true, HelpText = "input folder that contains images")]
        public string Input { get; set; }

        [Option('o', "output", Required = true, HelpText = "output folder to put the results")]
        public string Output { get; set; }

    }
}
