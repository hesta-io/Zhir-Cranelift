using System;
using CommandLine;

namespace CLI
{
    class CLIOptions
    {
        [Option('v', "verbose", Required = true, HelpText = "Test Test")]
        public bool Verbose { get; set; }

    }
}
