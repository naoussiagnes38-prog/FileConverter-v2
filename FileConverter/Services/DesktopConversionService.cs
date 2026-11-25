using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using FileConverter.Core.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace FileConverter.Services
{
    internal enum ConversionMethod
    {
        WordToPdf, ImageToPdf, ExcelToPdf, PdfToWord, PdfToImages, MergePdf, Jpg, Png, Bmp, PowerPointToPdf
    }

    internal static class DesktopConversionService
    {
        [ThreadStatic] private static CancellationToken operationToken;

        public static void Convert(string input, string output, ConversionMethod method, int quality, string renderer, Action<double> progress = null, CancellationToken token = default(CancellationToken))
        {
            operationToken = token;
            token.ThrowIfCancellationRequested();
            switch (method)
            {
                case ConversionMethod.PowerPointToPdf:
                    if (!TryLibreOffice(input, output, "impress_pdf_Export")) ConvertPowerPoint(input, output);
                    break;
                case ConversionMethod.WordToPdf:
                    if (!TryLibreOffice(input, output, "writer_pdf_Export")) ConvertOffice(input, output, false, false);
                    break;
                case ConversionMethod.ExcelToPdf:
                    if (!TryLibreOffice(input, output, "calc_pdf_Export")) ConvertOffice(input, output, true, false);
                    break;
                case ConversionMethod.PdfToWord:
                    RunDocumentWorker("to-word", input, output, null, null, null, progress, token);
                    break;
                case ConversionMethod.ImageToPdf: ImageToPdf(input, output, quality, progress); break;
                case ConversionMethod.PdfToImages: RenderPdf(input, output, quality, renderer, progress); break;
                case ConversionMethod.Jpg:
                    using (var source = Image.FromFile(input))
                    {
                        EnsureSingleFrame(source);
                        ConversionService.ApplyExifOrientation(source);
                        using (var stream = File.Create(output))
                            SaveJpeg(source, stream, quality);
                    }
                    break;
                case ConversionMethod.Png: new ConversionService().ConvertImage(input, output, "PNG"); break;
                case ConversionMethod.Bmp: new ConversionService().ConvertImage(input, output, "BMP"); break;
                default: throw new ArgumentException("Unsupported conversion.");
            }
        }

        public static ConversionMethod? PdfInputMethod(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".doc":
                case ".docx": return ConversionMethod.WordToPdf;
                case ".xls":
                case ".xlsx": return ConversionMethod.ExcelToPdf;
                case ".ppt":
                case ".pptx": return ConversionMethod.PowerPointToPdf;
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".gif":
                case ".tif":
                case ".tiff": return ConversionMethod.ImageToPdf;
                default: return null;
            }
        }

        internal static bool HasLibreOfficeRuntime()
        {
            try
            {
                RuntimePaths.Find("OfficeRuntime", "program/soffice.com");
                return true;
            }
            catch (FileNotFoundException) { return false; }
        }

        public static void MergePdf(IEnumerable<string> inputs, string output, Action<double> progress = null, CancellationToken token = default(CancellationToken))
        {
            var paths = inputs.ToArray();
            operationToken = token;
            if (HasPdfWorker())
            {
                string manifest = Path.GetTempFileName();
                try
                {
                    File.WriteAllLines(manifest, paths, new System.Text.UTF8Encoding(false));
                    RunPdfWorker("merge", manifest, output, 90, progress);
                }
                finally { File.Delete(manifest); }
                return;
            }
            throw new InvalidOperationException("Install the complete FileConverter package to merge PDFs with consistent page sizes.");
        }

        private static void ImageToPdf(string input, string output, int quality, Action<double> progress)
        {
            using (var image = Image.FromFile(input))
            {
                ConversionService.ApplyExifOrientation(image);
                var dimension = new FrameDimension(image.FrameDimensionsList[0]);
                int count = image.GetFrameCount(dimension);
                var builder = new PdfDocumentBuilder();
                for (int index = 0; index < count; index++)
                {
                    operationToken.ThrowIfCancellationRequested();
                    image.SelectActiveFrame(dimension, index);
                    double dpiX = image.HorizontalResolution > 0 ? image.HorizontalResolution : 96;
                    double dpiY = image.VerticalResolution > 0 ? image.VerticalResolution : 96;
                    double width = image.Width * 72.0 / dpiX;
                    double height = image.Height * 72.0 / dpiY;
                    var page = builder.AddPage(width, height);
                    using (var png = new MemoryStream())
                    {
                        // Lossless embedding avoids introducing JPEG artifacts or
                        // changing the source image colors during image-to-PDF.
                        image.Save(png, ImageFormat.Png);
                        page.AddPng(png.ToArray(), new PdfRectangle(0, 0, width, height));
                    }
                    progress?.Invoke((double)(index + 1) / count);
                }
                operationToken.ThrowIfCancellationRequested();
                File.WriteAllBytes(output, builder.Build());
            }
        }

        private static void EnsureSingleFrame(Image image)
        {
            if (image.FrameDimensionsList.Any(id => image.GetFrameCount(new FrameDimension(id)) > 1))
                throw new InvalidDataException("Select a single-frame image.");
        }

        private static void SaveJpeg(Image image, Stream output, int quality)
        {
            using (var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var options = new EncoderParameters(1))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(image, 0, 0, image.Width, image.Height);
                options.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
                var codec = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
                bitmap.Save(output, codec, options);
            }
        }

        internal static string ReadPdfText(string input, CancellationToken token = default(CancellationToken), int maxCharacters = int.MaxValue, Action<double> progress = null)
        {
            operationToken = token;
            if (HasPdfWorker())
            {
                string temporary = Path.GetTempFileName();
                try
                {
                    RunPdfWorker(maxCharacters == int.MaxValue ? "text" : "sample", input, temporary, 90, progress);
                    return File.ReadAllText(temporary, System.Text.Encoding.UTF8);
                }
                finally { File.Delete(temporary); }
            }
            using (var document = PdfDocument.Open(input))
            {
                var text = new System.Text.StringBuilder();
                int totalPages = Math.Max(1, document.NumberOfPages);
                int pageIndex = 0;
                foreach (var page in document.GetPages())
                {
                    token.ThrowIfCancellationRequested();
                    text.AppendLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page));
                    pageIndex++;
                    progress?.Invoke((double)pageIndex / totalPages);
                    if (text.Length >= maxCharacters) break;
                }
                return text.ToString(0, Math.Min(text.Length, maxCharacters));
            }
        }

        private static bool HasPdfWorker()
        {
            try { RuntimePaths.Find("TranslationRuntime", "python.exe"); return File.Exists(RuntimePaths.Script("document_worker.py")); }
            catch (FileNotFoundException) { return false; }
        }

        internal static void RunDocumentWorker(string operation, string input, string output, string format,
            string endpoint, string languages, Action<double> progress, CancellationToken token, Action<string> stage = null)
        {
            operationToken = token;
            string runtime = RuntimePaths.Find("TranslationRuntime", "python.exe");
            string office;
            try { office = RuntimePaths.Find("OfficeRuntime", "program/soffice.com"); }
            catch (FileNotFoundException) { office = ""; }
            var start = new ProcessStartInfo(Path.Combine(runtime, "python.exe"),
                "-B " + Quote(RuntimePaths.Script("layout_worker.py")) + " " + operation + " " + Quote(input) + " " + Quote(output) +
                " --format " + (format ?? "docx") + " --endpoint " + Quote(endpoint ?? "") +
                " --languages " + (languages ?? "en,fr") + " --office " + Quote(office));
            RunTool(start, progress, stage);
        }

        private static void RunPdfWorker(string operation, string input, string output, int quality, Action<double> progress)
        {
            string runtime = RuntimePaths.Find("TranslationRuntime", "python.exe");
            var start = new ProcessStartInfo(Path.Combine(runtime, "python.exe"),
                "-B " + Quote(RuntimePaths.Script("document_worker.py")) + " " + operation + " " +
                Quote(input) + " " + Quote(output) + " " + quality.ToString(CultureInfo.InvariantCulture));
            RunTool(start, progress);
        }

        private static string Quote(string value) { return "\"" + value + "\""; }

        private static readonly ConcurrentDictionary<int, Process> ActiveWorkers = new ConcurrentDictionary<int, Process>();
        internal static void StopAllWorkers()
        {
            foreach (var worker in ActiveWorkers.Values) StopWorker(worker);
        }

        private static void RunTool(ProcessStartInfo start, Action<double> progress, Action<string> stage = null)
        {
            operationToken.ThrowIfCancellationRequested();
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (var process = Process.Start(start))
            {
                ActiveWorkers.TryAdd(process.Id, process);
                try
                {
                long lastActivity = Stopwatch.GetTimestamp();
                var error = process.StandardError.ReadToEndAsync();
                var output = System.Threading.Tasks.Task.Run(async () =>
                {
                    int lastReported = -1;
                    string line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());
                        if (line.StartsWith("stage ", StringComparison.Ordinal)) { stage?.Invoke(line.Substring(6)); continue; }
                        string[] parts = line.Split(' ');
                        int page, total;
                        if (parts.Length == 2 && int.TryParse(parts[0], out page) &&
                            int.TryParse(parts[1], out total) && total > 0)
                        {
                            int percent = (int)(100.0 * page / total);
                            if (percent > lastReported) { lastReported = percent; progress?.Invoke((double)page / total); }
                        }
                    }
                });
                while (!process.WaitForExit(100))
                {
                    if (operationToken.IsCancellationRequested || (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastActivity)) / (double)Stopwatch.Frequency > 1800)
                    {
                        StopWorker(process);
                        operationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Document processing produced no progress for 30 minutes.");
                    }
                }
                operationToken.ThrowIfCancellationRequested();
                output.GetAwaiter().GetResult();
                string detail = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new IOException("Document processing failed: " + detail.Trim());
                }
                finally { Process ignored; ActiveWorkers.TryRemove(process.Id, out ignored); }
            }
        }

        private static void StopWorker(Process process)
        {
            try
            {
                if (process.HasExited) return;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using (var killer = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F")
                    { UseShellExecute = false, CreateNoWindow = true })) killer.WaitForExit(5000);
                }
                if (!process.HasExited) process.Kill();
                process.WaitForExit();
            }
            catch (InvalidOperationException) { }
        }

        private static bool TryLibreOffice(string input, string output, string filter)
        {
            string executable;
            try { executable = Path.Combine(RuntimePaths.Find("OfficeRuntime", "program/soffice.com"), "program", "soffice.com"); }
            catch (FileNotFoundException) { return false; }
            string work = Path.Combine(Path.GetTempPath(), "FileConverter-office-" + Guid.NewGuid().ToString("N"));
            string profile = Path.Combine(work, "profile");
            Directory.CreateDirectory(work);
            try
            {
                Directory.CreateDirectory(profile);
                string profileUri = new Uri(profile).AbsoluteUri;
                string exportOptions = @"{""UseLosslessCompression"":{""type"":""boolean"",""value"":""true""},""ExportHiddenSlides"":{""type"":""boolean"",""value"":""true""},""ExportBookmarks"":{""type"":""boolean"",""value"":""true""},""IsSkipEmptyPages"":{""type"":""boolean"",""value"":""false""}}";
                string exportFilter = "pdf:" + filter + ":" + exportOptions.Replace("\"", "\\\"");
                RunTool(new ProcessStartInfo(executable, "-env:UserInstallation=" + Quote(profileUri) +
                    " --headless --nologo --nodefault --norestore --convert-to " + exportFilter +
                    " --outdir " + Quote(work) + " " + Quote(input)), null);
                string generated = Path.Combine(work, Path.GetFileNameWithoutExtension(input) + ".pdf");
                if (!File.Exists(generated))
                    throw new IOException("The office engine could not read this document or produce a PDF. Check that it opens correctly and is not password protected.");
                File.Move(generated, output);
                return true;
            }
            finally { try { Directory.Delete(work, true); } catch (IOException) { } }
        }

        private static void RenderPdf(string input, string output, int quality, string renderer, Action<double> progress)
        {
            if ((string.IsNullOrWhiteSpace(renderer) || !File.Exists(renderer)) && HasPdfWorker())
            {
                RunPdfWorker("images", input, output, quality, progress);
                return;
            }
            string executable = renderer;
            if (string.IsNullOrWhiteSpace(executable))
            {
                string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pdftoppm.exe");
                executable = File.Exists(bundled) ? bundled : "pdftoppm.exe";
            }

            var start = new ProcessStartInfo(executable,
                "-progress -r 150 -jpeg -jpegopt quality=" + quality.ToString(CultureInfo.InvariantCulture) +
                " \"" + input + "\" \"" + Path.Combine(output, "page") + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            Process process;
            try { process = Process.Start(start); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException("Install Poppler or set PdfToPpmPath to pdftoppm.exe.", ex);
            }

            using (process)
            {
                ActiveWorkers.TryAdd(process.Id, process);
                try
                {
                long lastActivity = Stopwatch.GetTimestamp();
                var errors = new System.Text.StringBuilder();
                var reader = System.Threading.Tasks.Task.Run(async () =>
                {
                    string line;
                    while ((line = await process.StandardError.ReadLineAsync()) != null)
                    {
                        Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());
                        var fields = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                        int page, total;
                        if (fields.Length >= 2 && int.TryParse(fields[0], out page) &&
                            int.TryParse(fields[1], out total) && total > 0 && page > 0 && page <= total)
                            progress?.Invoke((double)page / total);
                        else if (errors.Length < 8192)
                            errors.AppendLine(line);
                    }
                });
                while (!process.WaitForExit(100))
                {
                    if (operationToken.IsCancellationRequested || (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastActivity)) / (double)Stopwatch.Frequency > 1800)
                    {
                        StopWorker(process);
                        operationToken.ThrowIfCancellationRequested();
                        throw new IOException("PDF rendering produced no progress for 30 minutes.");
                    }
                }
                operationToken.ThrowIfCancellationRequested();

                reader.GetAwaiter().GetResult();
                if (process.ExitCode != 0 || !Directory.EnumerateFiles(output, "*.jpg").Any())
                    throw new IOException("PDF rendering failed. " + errors.ToString().Trim());
                }
                finally { Process ignored; ActiveWorkers.TryRemove(process.Id, out ignored); }
            }
        }

        [ThreadStatic]
        private static Dictionary<string, object> officeApplications;

        internal static object GetOfficeApplication(string progId)
        {
            if (officeApplications == null)
                officeApplications = new Dictionary<string, object>();
            object application;
            if (!officeApplications.TryGetValue(progId, out application))
            {
                Type type = Type.GetTypeFromProgID(progId);
                if (type == null)
                    throw new InvalidOperationException("Install desktop " + progId + " for this conversion.");
                application = Activator.CreateInstance(type);
                officeApplications.Add(progId, application);
            }
            return application;
        }

        // Cleanup must never turn a completed export into a failed conversion.
        // Keep release arguments statically typed: dynamic binding can query
        // PowerPoint's type information after Quit has disconnected its RCW.
        internal static void CloseOfficeApplications()
        {
            var applications = officeApplications;
            officeApplications = null;
            if (applications == null)
                return;
            foreach (object application in applications.Values)
            {
                CloseOfficeObject(() => ((dynamic)application).Quit());
                ReleaseOfficeObject(application);
            }
        }

        private static void CloseOfficeObject(Action close)
        {
            try { close(); }
            catch (Exception ex) { Trace.TraceWarning("Office cleanup: " + ex.Message); }
        }

        private static void ReleaseOfficeObject(object value)
        {
            if (value == null)
                return;
            try
            {
                if (Marshal.IsComObject(value))
                    Marshal.FinalReleaseComObject(value);
            }
            catch (COMException ex) { Trace.TraceWarning("Office release: " + ex.Message); }
            catch (InvalidComObjectException ex) { Trace.TraceWarning("Office release: " + ex.Message); }
        }

        private static void ConvertPowerPoint(string input, string output)
        {
            Type type = Type.GetTypeFromProgID("PowerPoint.Application");
            if (type == null)
                throw new InvalidOperationException("Install desktop Microsoft PowerPoint to convert presentations.");

            dynamic application = GetOfficeApplication("PowerPoint.Application");
            dynamic presentations = null;
            dynamic presentation = null;
            try
            {
                application.AutomationSecurity = 3; // Disable macros.
                application.DisplayAlerts = 1; // Suppress interactive alerts.
                presentations = application.Presentations;
                presentation = presentations.Open(input, ReadOnly: -1, Untitled: 0, WithWindow: 0);
                operationToken.ThrowIfCancellationRequested();
                presentation.SaveAs(output, 32);
                operationToken.ThrowIfCancellationRequested(); // ppSaveAsPDF
            }
            finally
            {
                if ((object)presentation != null)
                    CloseOfficeObject(() => presentation.Close());
                ReleaseOfficeObject((object)presentation);
                ReleaseOfficeObject((object)presentations);
            }
        }

        private static void ConvertOffice(string input, string output, bool excel, bool toWord)
        {
            string progId = excel ? "Excel.Application" : "Word.Application";
            Type type = Type.GetTypeFromProgID(progId);
            if (type == null)
                throw new InvalidOperationException("Install desktop Microsoft " + (excel ? "Excel." : "Word (2013 or later)."));

            dynamic application = GetOfficeApplication(progId);
            dynamic documents = null;
            dynamic document = null;
            try
            {
                application.AutomationSecurity = 3; // Disable macros.
                if (excel)
                {
                    application.DisplayAlerts = false;
                    application.EnableEvents = false;
                    documents = application.Workbooks;
                    document = documents.Open(input, UpdateLinks: 0, ReadOnly: true, Password: "", AddToMru: false);
                    document.ExportAsFixedFormat(0, output);
                }
                else
                {
                    application.DisplayAlerts = 0;
                    application.Options.UpdateLinksAtOpen = false;
                    if (toWord && Version.Parse((string)application.Version).Major < 15)
                        throw new InvalidOperationException("PDF to Word requires Word 2013 or later.");
                    documents = application.Documents;
                    document = documents.Open(input, ConfirmConversions: false, ReadOnly: true,
                        AddToRecentFiles: false, PasswordDocument: "", Visible: false, NoEncodingDialog: true);
                    if (toWord)
                        document.SaveAs2(output, 16, AddToRecentFiles: false);
                    else
                        document.ExportAsFixedFormat(output, 17);
                }
            }
            finally
            {
                if ((object)document != null)
                    CloseOfficeObject(() => document.Close(false));
                ReleaseOfficeObject((object)document);
                ReleaseOfficeObject((object)documents);
            }
        }
    }
}
