using ImageProcessing;
using System;

namespace ClassLibraryTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            PreProcess pr = new PreProcess("../../../doc4.jpg");
            pr.Start();
            pr.GetProcessedImage().Save("../../../result-doc4.jpg");

            //PreProcess pr2 = new PreProcess("../../../doc2.jpg");
            //pr2.Start();
            //pr2.GetProcessedImage().Save("../../../result-doc2.jpg");
            Console.WriteLine("Done....");
        }
    }
}
