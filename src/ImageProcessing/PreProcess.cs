
using AForge.Imaging;
using AForge.Imaging.Filters;
using System.Drawing;

namespace ImageProcessing
{
    public class PreProcess
    {
        private UnmanagedImage InputImage { get; set; }
        public PreProcess(Bitmap srcImg)
        {
            InputImage = UnmanagedImage.FromManagedImage(srcImg);
        }
        public PreProcess(string imgPath)
        {
            InputImage = UnmanagedImage.FromManagedImage( new Bitmap(imgPath) );
        }
        public void Start()
        {
            Convert2Grayscale();
            CorrectContrastAndBrightness();
            ApplyMedian();
            FixOrientation();
            Sharpen();
        }
        private void Convert2Grayscale()
        {
            Grayscale filter = new Grayscale(0.2125, 0.7154, 0.0721);
            InputImage = filter.Apply(InputImage);
        }
        private void CorrectGamma()
        {
            // create filter
            GammaCorrection filter = new GammaCorrection(0.5);
            // apply the filter
            filter.ApplyInPlace(InputImage);
        }
        private void CorrectContrastAndBrightness()
        {
            BrightnessCorrection brightnessFilter = new BrightnessCorrection(0);
            brightnessFilter.ApplyInPlace(InputImage);
            ContrastCorrection contrastFilter = new ContrastCorrection(20);
            contrastFilter.ApplyInPlace(InputImage);
        }
        private void ApplyMedian()
        {
            Median filter = new Median();
            filter.ApplyInPlace(InputImage);
        }
        private void FixOrientation()
        {
            DocumentSkewChecker skewChecker = new DocumentSkewChecker();
            double angle = skewChecker.GetSkewAngle(InputImage);
            // create rotation filter
            RotateBilinear rotationFilter = new RotateBilinear(-angle);
            rotationFilter.FillColor = Color.White;
            
           this.InputImage = rotationFilter.Apply(InputImage);
        }
        private void EqualizeHistogram()
        {
            HistogramEqualization filter = new HistogramEqualization();
            filter.ApplyInPlace(InputImage);
        }
        private void Sharpen()
        {
            // create filter
            Sharpen filter = new Sharpen();
            // apply the filter
            filter.ApplyInPlace(InputImage);
        }
        public Bitmap GetProcessedImage()
        {
            return InputImage.ToManagedImage();
        }
    }
}
