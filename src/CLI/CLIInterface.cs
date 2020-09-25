using System;
using System.Collections.Generic;
using CommandLine;

namespace CLI
{
    public class CLIInterface
    {
      
        public static Dictionary<string,string> ParseArgs(string[] args)
        {
            Dictionary<string, string> parsedArguments = new Dictionary<string, string>();
            Parser.Default.ParseArguments<Options>(args)
               .WithParsed<Options>(o =>
               {
                 if (o.Input.Trim() != "")
                 {
                     parsedArguments.Add("input", o.Input);
                 }

                 if (o.Output.Trim() != "")
                 {
                     parsedArguments.Add("output", o.Output);
                 }
               });
               return parsedArguments;
;
        }
    }
}
