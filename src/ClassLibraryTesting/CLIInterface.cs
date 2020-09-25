using System;
using CommandLine;

namespace CLI
{
    public class CLIInterface
    {
      
        public static void ParseArgs(string[] args)
        {
            _ = Parser.Default.ParseArguments<CLIOptions>(args)
               .WithNotParsed <CLIOptions> (o =>
               {
                   if (o.Verbose)
                   {
                       Console.WriteLine("hahaha found you");
                   }
               })
;
        }
    }
}
