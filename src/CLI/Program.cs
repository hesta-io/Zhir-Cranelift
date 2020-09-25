using CLI;
using System;
using System.Collections.Generic;
using Worker;

namespace ClassLibraryTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            string input = "";
            string output = "";
            Dictionary<string,string> parsedArgs =  CLIInterface.ParseArgs(args);
            parsedArgs.TryGetValue("input", out input);
            parsedArgs.TryGetValue("output", out output);
            Console.WriteLine(input +" "+ output);
            /* PreProcess pr = new PreProcess("../../../doc4.jpg");
             pr.Start();
             pr.GetProcessedImage().Save("../../../result-doc4.jpg");
             */
        }
    }
}
