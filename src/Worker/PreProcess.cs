
using AForge.Imaging;
using AForge.Imaging.Filters;

using System;
using System.Collections.Generic;
using System.Drawing;

namespace Worker
{
    public class PreProcess : IDisposable
    {
        private UnmanagedImage InputImage { get; set; }

        public PreProcess(string imgPath)
        {
            InputImage = UnmanagedImage.FromManagedImage(AForge.Imaging.Image.FromFile(imgPath));
        }

        public void Start()
        {
            try
            {
                Convert2Grayscale();
            }
            catch (Exception e) { }
            Binarize();
            RemoveNoise();
            FixOrientation();
        }
        private void Convert2Grayscale()
        {
            Grayscale filter = new Grayscale(0.2125, 0.7154, 0.0721);
            InputImage = filter.Apply(InputImage);
        }
        private void CorrectContrastAndBrightness()
        {
            BrightnessCorrection brightnessFilter = new BrightnessCorrection(0);
            brightnessFilter.ApplyInPlace(InputImage);
            ContrastCorrection contrastFilter = new ContrastCorrection(20);
            contrastFilter.ApplyInPlace(InputImage);
        }
        private void RemoveNoise()
        {

            BilateralSmoothing filter = new BilateralSmoothing();
            filter.KernelSize = 7;
            filter.SpatialFactor = 10;
            filter.ColorFactor = 25;
            filter.ColorPower = 0.5;
            // apply the filter
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
        public void Binarize()
        {

            BradleyLocalThresholding bradly = new BradleyLocalThresholding();

            //UnmanagedImage diffImage = bradly.Apply(InputImage);
            //QuadrilateralFinder qf = new QuadrilateralFinder();
            //List<AForge.IntPoint> corners = qf.ProcessImage(diffImage);

            //SimpleQuadrilateralTransformation filter =
            //    new SimpleQuadrilateralTransformation(corners);
            //filter.UseInterpolation = true;
            //filter.AutomaticSizeCalculaton = true;
            //InputImage = filter.Apply(InputImage);
            bradly.ApplyInPlace(InputImage);
        }

        public void Dispose()
        {
            InputImage?.Dispose();
        }
    }
}
