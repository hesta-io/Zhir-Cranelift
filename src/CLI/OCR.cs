using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CLI
{
    public class OCR
    {
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
        public static string Run(string file)
        {
            var tesseractPath = "tesseract";

            var builder = new StringBuilder();

            var imageFile = File.ReadAllBytes(file);
            var text = ParseText(tesseractPath, imageFile, new string[] { "ckb", "ckb-destnus" });

            builder.AppendLine(text);

            return builder.ToString();
        }
    }
}
