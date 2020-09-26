using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;

namespace CLI
{
    public class CLIInterface
    {

        public static Dictionary<string, string> ParseArgs(string[] args)
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
            string input = "";
            string output = "";

            parsedArguments.TryGetValue("input", out input);
            parsedArguments.TryGetValue("output", out output);
            try
            {
                input = Path.GetFullPath(input);
                if (!Directory.Exists(input))
                {
                    Console.Error.WriteLine("invalid input path");
                    Environment.Exit(1);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("invalid input path");
                Environment.Exit(1);
            }

            try
            {
                output = Path.GetFullPath(output);
                if (!Directory.Exists(output))
                {
                    Console.Error.WriteLine("invalid output path");
                    Environment.Exit(1);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("invalid output path");
                Environment.Exit(1);
            }
            Dictionary<string, string> result = new Dictionary<string, string>();
            result.Add("input", input);
            result.Add("output", output);

            return result;
        }
    }
}
