using CLI;
using ImageProcessing;
using System;
using System.Linq;

namespace ClassLibraryTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            CLIInterface.ParseArgs(args);
           /* PreProcess pr = new PreProcess("../../../doc4.jpg");
            pr.Start();
            pr.GetProcessedImage().Save("../../../result-doc4.jpg");
*/
            Console.WriteLine("Done....");
        }
    }
}
