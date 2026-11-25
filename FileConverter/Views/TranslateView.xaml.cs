using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Configuration;
using System.Net.Http;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Documents;
using System.Xml.Linq;
using FileConverter.Core.Services;
using FileConverter.Services;

namespace FileConverter.Views
{
    public partial class TranslateView : UserControl
    {
        private ObservableCollection<TranslationFileItem> selectedFiles;

        public TranslateView()
        {
            InitializeComponent();

            selectedFiles = new ObservableCollection<TranslationFileItem>();

            FilesList.ItemsSource = selectedFiles;

            UpdateFileCount();
        }

        private void ResponsiveLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool narrow = e.NewSize.Width < 620;
            TranslationLayout.Margin = narrow ? new Thickness(6) : new Thickness(10);
            TranslationOutputFormat.Width = narrow ? Math.Max(150, e.NewSize.Width - 125) : 280;
        }

        private void CancelOperation_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this))
                ProgressLabel.Text = LocalizationManager.Text("Cancelling...");
            OperationSession.Cancel(this);
        }

        // Browse for documents
        private async void BrowseFiles_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            OpenFileDialog dialog = new OpenFileDialog();

            dialog.Title = "Select documents to translate";
            dialog.Multiselect = true;
            dialog.CheckFileExists = true;

            dialog.Filter =
                "Supported documents (*.docx;*.pdf;*.txt;*.rtf)|*.docx;*.pdf;*.txt;*.rtf|" +
                "Word documents (*.docx)|*.docx|" +
                "PDF documents (*.pdf)|*.pdf|" +
                "Text documents (*.txt)|*.txt|" +
                "Rich Text documents (*.rtf)|*.rtf|" +
                "All files (*.*)|*.*";

            if (dialog.ShowDialog() == true)
            {
                await AddFilesAsync(dialog.FileNames);
            }
        }

        // Handle drag entering
        private void FileDrop_DragEnter(
            object sender,
            DragEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        // Handle dragging over the upload area
        private void FileDrop_DragOver(
            object sender,
            DragEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        // Handle dropped files
        private async void FileDrop_Drop(
            object sender,
            DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] files =
                e.Data.GetData(DataFormats.FileDrop) as string[];

            if (files != null)
            {
                await AddFilesAsync(files);
            }
        }

        // Add supported documents
        private async Task AddFilesAsync(IEnumerable<string> files)
        {
            if (OperationSession.GetIsBusy(this)) return;
            var added = new List<TranslationFileItem>();
            int unsupportedCount = 0;

            foreach (string file in files)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                if (!File.Exists(file))
                {
                    continue;
                }

                if (!IsSupportedFile(file))
                {
                    unsupportedCount++;
                    continue;
                }

                bool alreadyAdded = selectedFiles.Any(
                    existing => string.Equals(
                        existing.FullPath,
                        file,
                        StringComparison.OrdinalIgnoreCase));

                if (alreadyAdded)
                {
                    continue;
                }

                FileInfo fileInfo = new FileInfo(file);

                TranslationFileItem item = new TranslationFileItem();

                item.FullPath = fileInfo.FullName;
                item.FileName = fileInfo.Name;
                item.FileInformation = fileInfo.Extension.ToUpperInvariant();
                try { item.FileInformation += " · " + GetFileSize(fileInfo.Length); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                item.DetectedLanguage = LocalizationManager.Text("Not detected");
                item.DetectedSourceLanguage = null;
                item.Status = LocalizationManager.Text("Queued");

                selectedFiles.Add(item);

                added.Add(item);
            }

            UpdateFileCount();

            if (unsupportedCount > 0)
            {
                MessageBox.Show(
                    unsupportedCount +
                    " unsupported document(s) were skipped.\n\n" +
                    "Supported formats: DOCX, PDF, TXT and RTF.",
                    "Unsupported documents",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (added.Count == 0)
            {
                MessageBox.Show(
                    "No new documents were added.",
                    "Translation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            if (added.Count == 0)
                return;

            var operation = OperationSession.Start(this);
            ProgressPanel.Visibility = Visibility.Visible;
            TranslationOverallProgress.Value = 0;
            TranslationOverallProgress.IsIndeterminate = false;
            try
            {
                var inspectionQueue = added.ToArray();
                for (int index = 0; index < inspectionQueue.Length; index++)
                {
                    var item = inspectionQueue[index];
                    if (!selectedFiles.Contains(item)) continue;
                    ShowDocumentProgress(item, index + 1, inspectionQueue.Length, "Preparing document...");
                    try
                    {
                        int inspectionIndex = index;
                        var inspectionProgress = new Progress<double>(fraction =>
                        {
                            if (operation.IsCancellationRequested) return;
                            item.ProgressPercent = (int)Math.Floor(Math.Max(0, Math.Min(1, fraction)) * 100);
                            TranslationOverallProgress.Value = 100.0 * (inspectionIndex + fraction) / inspectionQueue.Length;
                            ProgressLabel.Text = string.Format("{0}/{1} · {2} · {3}", inspectionIndex + 1, inspectionQueue.Length,
                                LocalizationManager.Text("Preparing document..."), item.FileName);
                        });
                        await InspectDocumentAsync(item, operation.Token, false, inspectionProgress);
                        item.Status = LocalizationManager.Text(item.EffectiveTargetLanguage == null
                            ? "Choose English or French as the target language." : "Queued");
                    }
                    catch (OperationCanceledException)
                    {
                        item.Status = LocalizationManager.Text("Cancelled");
                        HistoryView.Record(item.FullPath, null, "Cancelled", "Translate");
                        foreach (var pending in inspectionQueue.Skip(index + 1).Where(selectedFiles.Contains))
                        {
                            pending.Status = LocalizationManager.Text("Cancelled");
                            HistoryView.Record(pending.FullPath, null, "Cancelled", "Translate");
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        item.Status = ex.Message;
                    }
                    item.IsWorking = false;
                    item.IsActive = false;
                }
            }
            finally
            {
                foreach (var item in added) { item.IsWorking = false; item.IsActive = false; }
                ProgressPanel.Visibility = Visibility.Collapsed;
                OperationSession.Finish(this);
            }
        }

        // Check supported file extensions
        private bool IsSupportedFile(string file)
        {
            string extension =
                Path.GetExtension(file).ToLowerInvariant();

            return extension == ".docx" ||
                   extension == ".pdf" ||
                   extension == ".txt" ||
                   extension == ".rtf";
        }

        // Format file size
        private string GetFileSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            if (bytes < 1024 * 1024)
            {
                return string.Format(
                    "{0:0.##} KB",
                    bytes / 1024.0);
            }

            if (bytes < 1024L * 1024L * 1024L)
            {
                return string.Format(
                    "{0:0.##} MB",
                    bytes / (1024.0 * 1024.0));
            }

            return string.Format(
                "{0:0.##} GB",
                bytes / (1024.0 * 1024.0 * 1024.0));
        }

        // Update document count and empty state
        private void UpdateFileCount()
        {
            if (OutputLocation != null) OutputLocation.SetSources(selectedFiles.Select(file => file.FullPath));
            int count = selectedFiles.Count;

            if (count == 1)
            {
                FileCountText.Text = string.Format(LocalizationManager.Text("{0} document"), count);
            }
            else
            {
                FileCountText.Text = string.Format(LocalizationManager.Text("{0} documents"), count);
            }

            EmptyQueueText.Visibility = count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Remove an individual document
        private void RemoveFile_Click(
            object sender,
            RoutedEventArgs e)
        {
            Button button = sender as Button;

            if (button == null)
            {
                return;
            }

            string filePath = button.Tag as string;

            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            TranslationFileItem item = selectedFiles.FirstOrDefault(
                file => string.Equals(
                    file.FullPath,
                    filePath,
                    StringComparison.OrdinalIgnoreCase));

            if (item != null)
            {
                if (OperationSession.GetIsBusy(this) && item.IsActive) return;
                selectedFiles.Remove(item);
            }

            UpdateFileCount();
        }

        // Clear all selected documents
        private void ClearFiles_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            if (selectedFiles.Count == 0)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                "Are you sure you want to remove all selected documents?",
                "Clear documents",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                selectedFiles.Clear();
                ProgressPanel.Visibility = Visibility.Collapsed;
                UpdateFileCount();
            }
        }

        private async void TranslateFiles_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("Select at least one document.", "FileConverter");
                return;
            }

            string outputFolder;
            if (!OutputLocation.TryGetFolder(out outputFolder))
                return;

            string selectedFormat = (string)TranslationOutputFormat.SelectedValue ?? "pdf";
            var operationFiles = selectedFiles.ToArray();
            TranslationNotice.Visibility = Visibility.Collapsed;
            var operation = OperationSession.Start(this);
            foreach (var file in selectedFiles)
            {
                file.ProgressPercent = 0;
                file.IsWorking = false;
                file.Status = LocalizationManager.Text("Queued");
            }
            ProgressPanel.Visibility = Visibility.Visible;
            try
            {
                var endpoint = new Uri(ConfigurationManager.AppSettings["TranslationEndpoint"]);
                bool engineReady = false;
                int completed = 0;
                int failed = 0;
                int skipped = 0;
                int startedCount = 0;
                using (var service = new LanguageDetectionService(endpoint))
                {
                    for (int index = 0; index < operationFiles.Length; index++)
                    {
                        var item = operationFiles[index];
                        if (operation.IsCancellationRequested) break;
                        if (!selectedFiles.Contains(item))
                        {
                            TranslationOverallProgress.Value = 100.0 * (index + 1) / operationFiles.Length;
                            continue;
                        }
                        startedCount = index + 1;
                        string format = selectedFormat;
                        ShowDocumentProgress(item, index + 1, operationFiles.Length, "Preparing document...");
                        TranslationOverallProgress.IsIndeterminate = true;
                        string output = null;
                        bool success = false;
                        bool checkingEngine = false;
                        bool fileFinished = false;
                        try
                        {
                            int inspectionIndex = index;
                            var inspectionProgress = new Progress<double>(fraction =>
                            {
                                if (operation.IsCancellationRequested || !OperationSession.IsCurrent(this, operation)) return;
                                item.ProgressPercent = (int)Math.Floor(Math.Max(0, Math.Min(1, fraction)) * 100);
                                TranslationOverallProgress.IsIndeterminate = false;
                                TranslationOverallProgress.Value = 100.0 * (inspectionIndex + Math.Max(0, Math.Min(1, fraction))) / operationFiles.Length;
                                ProgressLabel.Text = string.Format("{0}/{1} · {2} · {3}", inspectionIndex + 1, operationFiles.Length,
                                    LocalizationManager.Text("Preparing document..."), item.FileName);
                            });
                            string text = await InspectDocumentAsync(item, operation.Token,
                                format == "txt" || Path.GetExtension(item.FullPath).Equals(".txt", StringComparison.OrdinalIgnoreCase), inspectionProgress);
                            string target = item.EffectiveTargetLanguage;
                            if (target == null)
                            {
                                item.Status = LocalizationManager.Text("Choose English or French as the target language.");
                                skipped++;
                            TranslationOverallProgress.IsIndeterminate = false;
                            TranslationOverallProgress.Value = 100.0 * (index + 1) / operationFiles.Length;
                                HistoryView.Record(item.FullPath, null, "Skipped", "Translate");
                                continue;
                            }

                            string source = item.EffectiveSourceLanguage;
                            if (!engineReady && source != target)
                            {
                                checkingEngine = true;
                                item.IsWorking = true;
                                ProgressLabel.Text = LocalizationManager.Text("Checking the local translation engine...");
                                try
                                {
                                    await LocalTranslationServer.EnsureReadyAsync(service, endpoint, () =>
                                        ProgressLabel.Text = LocalizationManager.Text("Starting LibreTranslate and loading language models..."), operation.Token);
                                }
                                catch (Exception ex)
                                {
                                    item.Status = string.Format(LocalizationManager.Text("Failed to translate: {0}"), ex.Message);
                                    checkingEngine = false;
                                    throw;
                                }
                                engineReady = true;
                                checkingEngine = false;
                            }

                            item.Status = LocalizationManager.Text("Translating...");
                            item.ProgressPercent = 0;
                            output = Path.Combine(outputFolder ?? Path.GetDirectoryName(item.FullPath),
                                Path.GetFileNameWithoutExtension(item.FullPath) + "." + target + "." + format);

                            ShowDocumentProgress(item, index + 1, operationFiles.Length, "Translating...");
                            int currentIndex = index;
                            Action<double> updateProgress = fraction =>
                            {
                                if (fileFinished || !OperationSession.IsCurrent(this, operation) || operation.IsCancellationRequested) return;
                                item.IsWorking = false;
                                double bounded = Math.Max(0, Math.Min(fraction, 0.99));
                                item.ProgressPercent = (int)Math.Floor(bounded * 100);
                                TranslationOverallProgress.IsIndeterminate = false;
                                TranslationOverallProgress.Value = 100.0 * (currentIndex + bounded) / operationFiles.Length;
                            };
                            if (format != "txt" && !Path.GetExtension(item.FullPath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                            {
                                int lastPercent = -1;
                                var uiProgress = new Progress<double>(updateProgress);
                                var uiStage = new Progress<string>(stage =>
                                {
                                    if (fileFinished || !OperationSession.IsCurrent(this, operation) || operation.IsCancellationRequested) return;
                                    item.Status = LocalizationManager.Text(stage);
                                    ShowDocumentProgress(item, currentIndex + 1, operationFiles.Length, stage);
                                    item.IsWorking = stage != "Translating..." || item.ProgressPercent == 0;
                                });
                                output = await Task.Run(() => BatchOperations.WriteOutput(output,
                                    temp => DesktopConversionService.RunDocumentWorker("translate", item.FullPath, temp, format,
                                        endpoint.AbsoluteUri, source + "," + target, fraction =>
                                        {
                                            int percent = (int)(fraction * 100);
                                            if (percent > lastPercent) { lastPercent = percent; ((IProgress<double>)uiProgress).Report(fraction); }
                                        }, operation.Token, stage => ((IProgress<string>)uiStage).Report(stage)), false, operation.Token));
                            }
                            else
                            {
                                string translated = await service.TranslateAsync(text, source, target, updateProgress, operation.Token);
                                ShowDocumentProgress(item, index + 1, operationFiles.Length, "Saving translation...");
                                output = await Task.Run(() => BatchOperations.WriteOutput(output,
                                    temp => TranslatedDocumentWriter.Write(temp, translated, format), false, operation.Token));
                            }
                            item.Status = LocalizationManager.Text("Saved: ") + output;
                            item.ProgressPercent = 100;
                            success = true;
                        }
                        catch (OperationCanceledException) when (operation.IsCancellationRequested)
                        {
                            item.Status = LocalizationManager.Text("Cancelled");
                            break;
                        }
                        catch (HttpRequestException ex) when (!checkingEngine)
                        {
                            item.Status = string.Format(LocalizationManager.Text(
                                "Lost the connection to LibreTranslate at {0}. {1}"), endpoint,
                                ex.GetBaseException().Message);
                        }
                        catch (TaskCanceledException) when (!checkingEngine)
                        {
                            item.Status = LocalizationManager.Text("LibreTranslate did not respond in time.");
                        }
                        catch (Exception ex) when (!checkingEngine)
                        {
                            item.Status = string.Format(LocalizationManager.Text("Failed to translate: {0}"), ex.Message);
                        }
                        finally
                        {
                            fileFinished = true;
                            item.IsWorking = false;
                            item.IsActive = false;
                            TranslationOverallProgress.IsIndeterminate = false;
                            if (!operation.IsCancellationRequested)
                                TranslationOverallProgress.Value = 100.0 * (index + 1) / operationFiles.Length;
                        }

                        if (success)
                            completed++;
                        else if (item.Status != LocalizationManager.Text("Cancelled"))
                            failed++;
                        HistoryView.Record(item.FullPath, output,
                            success ? "Completed" : item.Status == LocalizationManager.Text("Cancelled") ? "Cancelled" : "Failed",
                            "Translate");
                    }
                    if (operation.IsCancellationRequested)
                        foreach (var pending in operationFiles.Skip(startedCount).Where(selectedFiles.Contains))
                        {
                            pending.Status = LocalizationManager.Text("Cancelled");
                            HistoryView.Record(pending.FullPath, null, "Cancelled", "Translate");
                        }
                    ProgressLabel.Text = (operation.IsCancellationRequested ? LocalizationManager.Text("Cancelled") + " · " : "") + string.Format(LocalizationManager.Text("{0} completed, {1} failed, {2} skipped."),
                        completed, failed, skipped);
                }
            }
            catch (OperationCanceledException) { ProgressLabel.Text = LocalizationManager.Text("Cancelled"); }
            catch (Exception ex)
            {
                ProgressLabel.Text = LocalizationManager.Text("Translation stopped.");
                TranslationNotice.Text = ex.Message;
                TranslationNotice.Visibility = Visibility.Visible;
            }
            finally
            {
                OperationSession.Finish(this);
                MessageBox.Show(ProgressLabel.Text + "\n" + string.Join("\n", selectedFiles.Take(5).Select(file =>
                    file.FileName + " · " + file.Status)) + (TranslationNotice.Visibility == Visibility.Visible ? "\n" + TranslationNotice.Text : ""), "FileConverter");
                selectedFiles.Clear();
                TranslationOverallProgress.Value = 0;
                TranslationOverallProgress.IsIndeterminate = false;
                ProgressLabel.Text = "";
                ProgressPanel.Visibility = Visibility.Collapsed;
                TranslationNotice.Visibility = Visibility.Collapsed;
                UpdateFileCount();
            }
        }

        private static async Task<string> InspectDocumentAsync(TranslationFileItem item, CancellationToken token = default(CancellationToken), bool fullText = false, IProgress<double> progress = null)
        {
            token.ThrowIfCancellationRequested();
            var information = new FileInfo(item.FullPath);
            long length = information.Length;
            DateTime modified = information.LastWriteTimeUtc;
            if (item.CachedText != null && (!fullText || item.CachedIsComplete) && item.CachedLength == length && item.CachedModified == modified)
                return item.CachedText;

            item.CachedText = null;
            item.Status = LocalizationManager.Text("Preparing document...");
            item.DetectedLanguage = LocalizationManager.Text("Not detected");
            item.DetectedSourceLanguage = null;

            string text = Path.GetExtension(item.FullPath).Equals(".rtf", StringComparison.OrdinalIgnoreCase)
                ? await ReadRtfAsync(item.FullPath, token)
                : await Task.Run(() => ReadDocument(item.FullPath, token, fullText ? int.MaxValue : 16000,
                    progress == null ? null : new Action<double>(fraction => progress.Report(fraction))));
            token.ThrowIfCancellationRequested();
            if (!fullText && text.Length > 16000) text = text.Substring(0, 16000);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException(Path.GetExtension(item.FullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? LocalizationManager.Text("This PDF has no selectable text. Run OCR on scanned pages and try again.")
                    : "The document has no readable text.");

            string source = await Task.Run(() => DocumentLanguageDetector.Detect(text));
            item.DetectedSourceLanguage = source;
            item.DetectedLanguage = source == "en" ? LocalizationManager.Text("English") :
                source == "fr" ? LocalizationManager.Text("French") : source ?? LocalizationManager.Text("Unknown");
            information.Refresh();
            if (text.Length <= 2000000 && information.Exists && information.Length == length && information.LastWriteTimeUtc == modified)
            {
                item.CachedLength = length;
                item.CachedModified = modified;
                item.CachedText = text;
                item.CachedIsComplete = fullText;
            }
            return text;
        }

        private static async Task<string> ReadRtfAsync(string path, CancellationToken token)
        {
            var result = new TaskCompletionSource<string>();
            var thread = new Thread(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    string text = ReadRtf(path);
                    token.ThrowIfCancellationRequested();
                    result.TrySetResult(text);
                }
                catch (OperationCanceledException) { result.TrySetCanceled(); }
                catch (Exception ex) { result.TrySetException(ex); }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            using (token.Register(() => result.TrySetCanceled()))
                return await result.Task;
        }

        private static string ReadRtf(string path)
        {
            var document = new FlowDocument();
            var range = new TextRange(document.ContentStart, document.ContentEnd);
            using (var input = File.OpenRead(path))
                range.Load(input, DataFormats.Rtf);
            return range.Text;
        }

        private static string ReadDocument(string path, CancellationToken token = default(CancellationToken), int maxCharacters = int.MaxValue, Action<double> progress = null)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".txt":
                    try { return ReadText(path, new UTF8Encoding(false, true), maxCharacters, token); }
                    catch (DecoderFallbackException) { return ReadText(path, Encoding.GetEncoding(1252), maxCharacters, token); }
                case ".docx":
                    using (var zip = ZipFile.OpenRead(path))
                    {
                        var entry = zip.GetEntry("word/document.xml");
                        if (entry == null)
                            throw new InvalidDataException("Missing DOCX document body.");

                        using (var input = entry.Open())
                        using (var reader = System.Xml.XmlReader.Create(input, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit }))
                        {
                            var text = new StringBuilder();
                            bool insideText = false;
                            while (reader.Read() && text.Length < maxCharacters)
                            {
                                token.ThrowIfCancellationRequested();
                                if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "t") insideText = !reader.IsEmptyElement;
                                else if (reader.NodeType == System.Xml.XmlNodeType.EndElement && reader.LocalName == "t") insideText = false;
                                else if (reader.NodeType == System.Xml.XmlNodeType.EndElement && reader.LocalName == "p") text.AppendLine();
                                else if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "tab") text.Append('\t');
                                else if (reader.NodeType == System.Xml.XmlNodeType.Element && (reader.LocalName == "br" || reader.LocalName == "cr")) text.AppendLine();
                                else if (insideText && (reader.NodeType == System.Xml.XmlNodeType.Text || reader.NodeType == System.Xml.XmlNodeType.SignificantWhitespace)) text.Append(reader.Value);
                            }
                            return text.ToString(0, Math.Min(text.Length, maxCharacters));
                        }
                    }
                case ".pdf":
                    return DesktopConversionService.ReadPdfText(path, token, maxCharacters, progress);
                default:
                    throw new InvalidDataException("Unsupported document extension.");
            }
        }

        private static string ReadText(string path, Encoding encoding, int limit, CancellationToken token)
        {
            using (var reader = new StreamReader(path, encoding, true))
            {
                var text = new StringBuilder();
                var buffer = new char[Math.Min(8192, limit)];
                while (text.Length < limit)
                {
                    token.ThrowIfCancellationRequested();
                    int read = reader.Read(buffer, 0, Math.Min(buffer.Length, limit - text.Length));
                    if (read == 0) break;
                    text.Append(buffer, 0, read);
                }
                return text.ToString();
            }
        }

        private static string ReadPdf(string path)
        {
            return DesktopConversionService.ReadPdfText(path);
        }

        private void ShowDocumentProgress(TranslationFileItem item, int current, int total, string stage)
        {
            item.IsWorking = true;
            item.IsActive = true;
            ProgressLabel.Text = string.Format("{0}/{1} · {2} · {3}", current, total,
                LocalizationManager.Text(stage), item.FileName);

        }

    }

    // Information displayed for each translation document
    public class TranslationFileItem : INotifyPropertyChanged
    {
        public string FullPath { get; set; }

        public string FileName { get; set; }

        public string FileInformation { get; set; }

        internal string CachedText { get; set; }
        internal bool CachedIsComplete { get; set; }
        internal long CachedLength { get; set; }
        internal DateTime CachedModified { get; set; }

        private string targetLanguage = "";
        private string detectedSourceLanguage;

        public string TargetLanguage
        {
            get { return targetLanguage; }
            set
            {
                if (targetLanguage == value)
                    return;
                SetValue(ref targetLanguage, value);
                NotifyDirectionChanged();
            }
        }

        internal string DetectedSourceLanguage
        {
            get { return detectedSourceLanguage; }
            set
            {
                detectedSourceLanguage = value;
                if (targetLanguage == "" && (value == "en" || value == "fr"))
                    TargetLanguage = value == "en" ? "fr" : "en";
                NotifyDirectionChanged();
            }
        }

        internal string EffectiveTargetLanguage
        {
            get
            {
                if (targetLanguage == "en" || targetLanguage == "fr")
                    return targetLanguage;
                return detectedSourceLanguage == "en" ? "fr" : detectedSourceLanguage == "fr" ? "en" : null;
            }
        }

        internal string EffectiveSourceLanguage
        {
            get
            {
                return detectedSourceLanguage == "en" || detectedSourceLanguage == "fr"
                    ? detectedSourceLanguage : EffectiveTargetLanguage == "fr" ? "en" : "fr";
            }
        }

        private void NotifyDirectionChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationDirection)));
        }

        private bool isWorking;
        public bool IsWorking
        {
            get { return isWorking; }
            set { isWorking = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWorking))); }
        }
        private bool isActive;
        public bool IsActive
        {
            get { return isActive; }
            set { isActive = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRemove))); }
        }
        public bool CanRemove { get { return !IsActive; } }
        private int progressPercent;
        public int ProgressPercent
        {
            get { return progressPercent; }
            set { progressPercent = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressPercent))); }
        }

        private string detectedLanguage;
        private string status;

        public string DetectedLanguage
        {
            get { return detectedLanguage; }
            set { SetValue(ref detectedLanguage, value); }
        }

        public string TranslationDirection
        {
            get
            {
                string target = EffectiveTargetLanguage;
                if (target == null) return LocalizationManager.Text("Pending");
                return LocalizationManager.Text(EffectiveSourceLanguage == "fr" ? "French" : "English") +
                    " → " + LocalizationManager.Text(target == "fr" ? "French" : "English");
            }
        }

        public string Status
        {
            get { return status; }
            set { SetValue(ref status, value); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetValue(ref string field, string value, [CallerMemberName] string property = null)
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
