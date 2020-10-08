using Microsoft.Win32;

using PDFiumSharp;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DemoApp
{
    public class Model
    {
        public string Name { get; set; }
        public bool IsChecked { get; set; }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private CancellationTokenSource _cancellationTokenSource;
        private Model[] _models;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            modelsList.ItemsSource = _models = Directory.EnumerateFiles("tesseract\\tessdata", "ckb*.traineddata")
                              .Select(f => Path.GetFileNameWithoutExtension(f))
                              .Select(f => new Model
                              {
                                  Name = f,
                                  IsChecked = false
                              })
                              .ToArray();
        }

        //private async void button_Click(object sender, RoutedEventArgs e)
        //{
        //    var fileDialog = new OpenFileDialog();
        //    fileDialog.Filter = "PDF documents|*.pdf";
        //    fileDialog.CheckFileExists = true;
        //    var result = fileDialog.ShowDialog();
        //    if (result != true) return;

        //    var folderDialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
        //    result = folderDialog.ShowDialog();
        //    if (result != true) return;
        //    var folder = folderDialog.SelectedPath;

        //    var pdfPath = fileDialog.FileName;
        //    RasterizePDF(folder, pdfPath);

        //    var saveDialog = new SaveFileDialog();
        //    var document = RunOCR(folder);
        //    saveDialog.Filter = "Text documents|*.txt";
        //    result = saveDialog.ShowDialog();
        //    if (result != true) return;

        //    File.WriteAllText(saveDialog.FileName, document);
        //}

        private string RunOCR(string file)
        {
            var tesseractPath = "tesseract";

            var builder = new StringBuilder();

            var imageFile = File.ReadAllBytes(file);
            var text = ParseText(tesseractPath, imageFile, _models.Where(m => m.IsChecked).Select(m => m.Name).ToArray());

            builder.AppendLine(text);
            builder.AppendLine("----------------");

            return builder.ToString();
        }

        private static Task RasterizePDF(string folder, string pdfPath)
        {
            var document = new PdfDocument(pdfPath);

            var tasks = document.Pages.Select(p => Task.Run(() => RasterizePage(folder, p)));

            return Task.WhenAll(tasks);
        }

        private static void RasterizePage(string folder, PdfPage page)
        {
            const int factor = 8;
            var bitmap = new PDFiumBitmap((int)page.Width * factor, (int)page.Height * factor, true);

            page.Render(bitmap, orientation: PDFiumSharp.Enums.PageOrientations.Normal,
                            flags: PDFiumSharp.Enums.RenderingFlags.Grayscale);

            var path = Path.Combine(folder, $"{page.Index}.jpg");

            bitmap.Save(path);
            bitmap.Dispose();

            using (var input = new MemoryStream())
            using (var output = new MemoryStream())
            {
                using (var file = File.OpenRead(path))
                {
                    file.CopyTo(input);
                }

                var b = new BitmapImage();
                b.BeginInit();
                b.StreamSource = input;
                b.EndInit();

                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(ReplaceTransparency(b, Colors.White)));
                encoder.Save(output);

                File.WriteAllBytes(path, output.ToArray());
            }
        }

        // https://stackoverflow.com/a/24039841/7003797
        private static BitmapSource ReplaceTransparency(BitmapSource bitmap, Color color)
        {
            var rect = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
            var visual = new DrawingVisual();
            var context = visual.RenderOpen();
            context.DrawRectangle(new SolidColorBrush(color), null, rect);
            context.DrawImage(bitmap, rect);
            context.Close();

            var render = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight,
                96, 96, PixelFormats.Pbgra32);
            render.Render(visual);
            return render;
        }

        private static string ParseText(string tesseractPath, byte[] imageFile, params string[] lang)
        {
            string output = string.Empty;
            var tempOutputFile = Path.GetTempPath() + Guid.NewGuid();
            var tempImageFile = Path.GetTempFileName();

            var stdoutBuilder = new StringBuilder();

            try
            {
                File.WriteAllBytes(tempImageFile, imageFile);

                ProcessStartInfo info = new ProcessStartInfo();
                info.WorkingDirectory = tesseractPath;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                info.UseShellExecute = false;
                info.FileName = "cmd.exe";
                info.Arguments =
                    "/c tesseract.exe " +
                    // Image file.
                    tempImageFile + " " +
                    // Output file (tesseract add '.txt' at the end)
                    tempOutputFile +
                    // Languages.
                    " -l " + string.Join("+", lang);
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                // Start tesseract.
                Process process = Process.Start(info);
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    // Exit code: success.
                    output = File.ReadAllText(tempOutputFile + ".txt");
                }
                else
                {
                    stdoutBuilder.AppendLine(process.StandardOutput.ReadToEnd());
                    stdoutBuilder.AppendLine(process.StandardError.ReadToEnd());

                    throw new Exception("Error. Tesseract stopped with an error code = " +
                        process.ExitCode + Environment.NewLine + stdoutBuilder.ToString());
                }
            }
            finally
            {
                File.Delete(tempImageFile);
                File.Delete(tempOutputFile + ".txt");
            }

            return output;
        }

        private async void ocrButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderDialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
                var result = folderDialog.ShowDialog();
                if (result != true) return;
                var folder = folderDialog.SelectedPath;

                StartProgress();

                _cancellationTokenSource = new CancellationTokenSource();
                var progress = new Progress<double>();
                progress.ProgressChanged += (s, args) => progressBar.Value = args;

                StartProgress();

                var tasks = Directory.EnumerateFiles(folder, "*.*")
                    .Select(f => Task.Run(() => RunOCR(f)))
                    .ToArray();

                await Task.WhenAll(tasks);

                var builder = new StringBuilder();

                foreach (var task in tasks)
                {
                    builder.AppendLine(task.Result);
                }

                logTextBox.Text = builder.ToString();
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                EndProgress();
            }
        }

        private async void extractButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var fileDialog = new OpenFileDialog();
                fileDialog.Filter = "PDF documents|*.pdf";
                fileDialog.CheckFileExists = true;
                var result = fileDialog.ShowDialog();
                if (result != true) return;

                var folderDialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
                result = folderDialog.ShowDialog();
                if (result != true) return;
                var folder = folderDialog.SelectedPath;

                StartProgress();
                progressGrid.Visibility = Visibility.Collapsed;

                var pdfPath = fileDialog.FileName;
                await RasterizePDF(folder, pdfPath);
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                EndProgress();
            }
        }

        private void StartProgress()
        {
            progressBar.Value = 0;
            ocrButton.IsEnabled = false;
            extractButton.IsEnabled = false;
            //progressGrid.Visibility = Visibility.Visible;
        }

        private void EndProgress()
        {
            ocrButton.IsEnabled = true;
            extractButton.IsEnabled = true;
            progressGrid.Visibility = Visibility.Collapsed;
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }
        }
    }
}
