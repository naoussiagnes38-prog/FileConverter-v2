using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FileConverter.Services;

namespace FileConverter.Views
{
    public partial class ConvertView : UserControl
    {
        private readonly ObservableCollection<ConversionRow> selectedFiles = new ObservableCollection<ConversionRow>();
        private ConversionMethod? method;
        private bool running;
        private CancellationTokenSource operation;
        private BlockingCollection<Action> officeQueue;
        private TaskCompletionSource<bool> officeStopped;
        private int processedFiles;

        public ConvertView()
        {
            InitializeComponent();
            FileList.ItemsSource = selectedFiles;
            UpdateFileCount();
        }

        private void ResponsiveLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool narrow = e.NewSize.Width < 620;
            ConversionContent.Margin = narrow ? new Thickness(10) : new Thickness(16);
        }

        private void CancelOperation_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this))
                BatchStatus.Text = LocalizationManager.Text("Cancelling...");
            OperationSession.Cancel(this);
        }

        private void Method_Checked(object sender, RoutedEventArgs e)
        {
            method = (ConversionMethod)Enum.Parse(typeof(ConversionMethod), (string)((RadioButton)sender).Tag);
            QualityOptions.Visibility = method == ConversionMethod.PdfToImages || method == ConversionMethod.Jpg ? Visibility.Visible : Visibility.Collapsed;
            UpdateFileCount();
        }

        private async void ConvertFiles_Click(object sender, RoutedEventArgs e)
        {
            if (running || !method.HasValue || selectedFiles.Count == 0)
                return;

            var files = selectedFiles.ToArray();
            var conversion = method.Value;
            if (conversion == ConversionMethod.MergePdf && files.Length < 2)
            {
                BatchStatus.Text = LocalizationManager.Text("Add at least two files to merge.");
                return;
            }

            string outputFolder;
            if (!OutputLocation.TryGetFolder(out outputFolder))
                return;

            operation = OperationSession.Start(this);
            int quality = (int)ImageQuality.Value;
            string renderer = ConfigurationManager.AppSettings["PdfToPpmPath"];
            officeQueue = new BlockingCollection<Action>();
            officeStopped = new TaskCompletionSource<bool>();
            var officeThread = new Thread(() =>
            {
                try
                {
                    foreach (var action in officeQueue.GetConsumingEnumerable())
                        action();
                }
                finally
                {
                    try { DesktopConversionService.CloseOfficeApplications(); }
                    finally { officeStopped.SetResult(true); }
                }
            });
            officeThread.IsBackground = true;
            officeThread.SetApartmentState(ApartmentState.STA);
            officeThread.Start();
            running = true;
            UpdateFileCount();

            processedFiles = 0;
            OverallProgress.Value = 0;
            BatchStatus.Text = LocalizationManager.Text("Converting...");
            foreach (var file in files)
                file.Update(LocalizationManager.Text("Queued"), false, 0);

            try
            {
                if (conversion == ConversionMethod.MergePdf)
                    await MergeAsync(files, quality, outputFolder);
                else
                {
                    using (var workers = new SemaphoreSlim(2))
                        await Task.WhenAll(files.Select(file => ConvertAsync(file, conversion, quality, renderer, outputFolder, workers)));
                }

                var remainingFiles = files.Where(file => !file.WasRemoved).ToArray();
                BatchStatus.Text = operation.IsCancellationRequested ? LocalizationManager.Text("Cancelled") : string.Format(LocalizationManager.Text("{0} completed, {1} failed."),
                    remainingFiles.Count(file => file.HasOutput), remainingFiles.Count(file => !file.HasOutput));
            }
            catch (OperationCanceledException)
            {
                foreach (var file in files.Where(file => !file.WasRemoved && !file.HasOutput && !file.StatusText.StartsWith(LocalizationManager.Text("Failed"))))
                {
                    file.Update(LocalizationManager.Text("Cancelled"), false, file.Progress);
                    HistoryView.Record(file.FilePath, null, "Cancelled", "Convert");
                }
                BatchStatus.Text = LocalizationManager.Text("Cancelled");
            }
            catch (Exception ex)
            {
                BatchStatus.Text = ex.Message;
            }
            finally
            {
                officeQueue.CompleteAdding();
                await officeStopped.Task;
                officeQueue.Dispose();
                officeQueue = null;
                running = false;
                OverallProgress.IsIndeterminate = false;
                OperationSession.Finish(this);
                MessageBox.Show(BatchStatus.Text + "\n" + string.Join("\n", files.Where(file => !file.WasRemoved).Take(5).Select(file =>
                    file.OutputPath ?? file.FileName + " · " + file.StatusText)), "FileConverter");
                selectedFiles.Clear();
                OverallProgress.Value = 0;
                BatchStatus.Text = "";
                UpdateFileCount();
            }
        }

        private async Task ConvertAsync(ConversionRow file, ConversionMethod conversion, int quality,
            string renderer, string outputFolder, SemaphoreSlim workers)
        {
            var session = operation;
            bool fileFinished = false;
            int reportedPercent = -1;
            await workers.WaitAsync();
            try
            {
                if (!selectedFiles.Contains(file)) return;
                operation.Token.ThrowIfCancellationRequested();
                file.IsActive = true;
                file.Update(LocalizationManager.Text("Converting..."), true, 0);
                UpdateOverallProgress();
                ValidateInput(file.FilePath, conversion);
                bool images = conversion == ConversionMethod.PdfToImages;
                string suffix = images ? ".pages" : conversion == ConversionMethod.PdfToWord ? ".docx" :
                    conversion == ConversionMethod.Jpg ? ".jpg" :
                    conversion == ConversionMethod.Png ? ".png" :
                    conversion == ConversionMethod.Bmp ? ".bmp" : ".pdf";
                string outputName = Path.GetFileNameWithoutExtension(file.FilePath) + suffix;
                string requested = Path.Combine(outputFolder ?? Path.GetDirectoryName(file.FilePath), outputName);
                string output = await RunConversionAsync(conversion, () => BatchOperations.WriteOutput(requested,
                    temp => DesktopConversionService.Convert(file.FilePath, temp, conversion, quality, renderer, fraction =>
                    {
                        int percent = (int)Math.Floor(Math.Min(fraction, 0.99) * 100);
                        if (percent <= reportedPercent) return;
                        reportedPercent = percent;
                        Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (fileFinished || !OperationSession.IsCurrent(this, session) || session.IsCancellationRequested || file.HasOutput || file.Progress == 100)
                            return;
                        file.SetProgress(percent);
                        UpdateOverallProgress();
                    }));
                    }, operation.Token), images, operation.Token));
                Finish(file, output, null);
            }
            catch (OperationCanceledException)
            {
                file.Update(LocalizationManager.Text("Cancelled"), false, file.Progress);
                HistoryView.Record(file.FilePath, null, "Cancelled", "Convert");
            }
            catch (Exception ex)
            {
                Finish(file, null, ex.Message);
            }
            finally
            {
                fileFinished = true;
                file.IsActive = false;
                UpdateOverallProgress();
                workers.Release();
            }
        }

        private async Task MergeAsync(ConversionRow[] files, int quality, string outputFolder)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "FileConverter-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                string[] prepared;
                using (var workers = new SemaphoreSlim(2))
                    prepared = await Task.WhenAll(files.Select((file, index) =>
                        PrepareMergeAsync(file, Path.Combine(tempDirectory, index + ".pdf"), quality, workers)));

                operation.Token.ThrowIfCancellationRequested();
                var ready = prepared.Where(path => path != null).ToArray();
                var readyFiles = files.Where((file, index) => prepared[index] != null).ToArray();
                var session = operation;
                bool mergeFinished = false;
                string output = null;
                string error = LocalizationManager.Text("At least two readable files are needed to merge.");
                if (ready.Length >= 2)
                {
                    try
                    {
                        foreach (var file in readyFiles) file.IsActive = true;
                        foreach (var file in readyFiles)
                            file.Update(LocalizationManager.Text("Converting..."), true, file.Progress);
                        string requested = Path.Combine(outputFolder ?? Path.GetDirectoryName(files[0].FilePath), "merged.pdf");
                        output = await Task.Run(() => BatchOperations.WriteOutput(requested,
                            temp => DesktopConversionService.MergePdf(ready, temp, fraction => Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (mergeFinished || !OperationSession.IsCurrent(this, session) || session.IsCancellationRequested) return;
                                foreach (var file in readyFiles) file.SetProgress(50 + (int)(49 * fraction));
                                UpdateOverallProgress();
                            })), operation.Token), false, operation.Token));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { error = ex.Message; }
                    finally
                    {
                        mergeFinished = true;
                        foreach (var file in readyFiles) file.IsActive = false;
                    }
                }

                for (int index = 0; index < files.Length; index++)
                    if (prepared[index] != null)
                        Finish(files[index], output, error);
            }
            finally
            {
                try { Directory.Delete(tempDirectory, true); }
                catch (IOException ex) { Trace.TraceWarning(ex.Message); }
                catch (UnauthorizedAccessException ex) { Trace.TraceWarning(ex.Message); }
            }
        }

        private async Task<string> PrepareMergeAsync(ConversionRow file, string temp, int quality, SemaphoreSlim workers)
        {
            await workers.WaitAsync();
            try
            {
                if (!selectedFiles.Contains(file)) return null;
                operation.Token.ThrowIfCancellationRequested();
                file.IsActive = true;
                file.Update(LocalizationManager.Text("Converting..."), true, 0);
                UpdateOverallProgress();
                if (Path.GetExtension(file.FilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    await Task.Run(() => DesktopConversionService.MergePdf(new[] { file.FilePath }, temp, null, operation.Token));
                else
                {
                    var conversion = DesktopConversionService.PdfInputMethod(file.FilePath);
                    if (!conversion.HasValue)
                        throw new InvalidDataException(LocalizationManager.Text("This file type cannot be merged."));
                    await RunConversionAsync(conversion.Value, () =>
                    {
                        DesktopConversionService.Convert(file.FilePath, temp, conversion.Value, quality, null, null, operation.Token);
                        return temp;
                    });
                }
                file.Update(LocalizationManager.Text("Waiting for merge..."), false, 50);
                return temp;
            }
            catch (OperationCanceledException)
            {
                file.Update(LocalizationManager.Text("Cancelled"), false, file.Progress);
                return null;
            }
            catch (Exception ex)
            {
                Finish(file, null, ex.Message);
                return null;
            }
            finally
            {
                file.IsActive = false;
                workers.Release();
            }
        }

        private Task<string> RunConversionAsync(ConversionMethod conversion, Func<string> work)
        {
            bool officeExport = conversion == ConversionMethod.WordToPdf ||
                conversion == ConversionMethod.ExcelToPdf || conversion == ConversionMethod.PowerPointToPdf;
            if (!officeExport || DesktopConversionService.HasLibreOfficeRuntime())
                return Task.Run(work);

            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            officeQueue.Add(() =>
            {
                try { operation.Token.ThrowIfCancellationRequested(); completion.SetResult(work()); }
                catch (Exception ex) { completion.SetException(ex); }
            });
            return completion.Task;
        }

        private static void ValidateInput(string path, ConversionMethod conversion)
        {
            ConversionMethod? input = DesktopConversionService.PdfInputMethod(path);
            bool valid = conversion == ConversionMethod.PdfToWord || conversion == ConversionMethod.PdfToImages
                ? Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                : conversion == ConversionMethod.Jpg || conversion == ConversionMethod.Png || conversion == ConversionMethod.Bmp
                    ? input == ConversionMethod.ImageToPdf : input == conversion;
            if (!valid)
                throw new InvalidDataException(LocalizationManager.Text("This file type does not match the selected conversion."));
        }

        private void UpdateOverallProgress()
        {
            // Keep the batch bar determinate so completed/per-file fractions remain
            // visible. Rows use their own indeterminate state when an Office export
            // does not expose measurable progress.
            OverallProgress.IsIndeterminate = false;
            OverallProgress.Value = selectedFiles.Count == 0 ? 0 :
                selectedFiles.Average(file => file.Progress);
        }

        private void Finish(ConversionRow file, string output, string error)
        {
            file.Update(output == null ? LocalizationManager.Text("Failed") + " · " + error :
                LocalizationManager.Text("Done") + " · " + Path.GetFileName(output), false, output == null ? file.Progress : 100, output);
            processedFiles++;
            UpdateOverallProgress();
            BatchStatus.Text = string.Format(LocalizationManager.Text("{0} of {1} files processed"),
                processedFiles, selectedFiles.Count);
            HistoryView.Record(file.FilePath, output, output != null, "Convert");
        }

        private void ChooseFiles_Click(object sender, RoutedEventArgs e)
        {
            if (running) return;
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Supported files|*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files|*.*"
            };
            if (dialog.ShowDialog() == true)
                AddFiles(dialog.FileNames);
        }

        private void AddFiles(string[] paths)
        {
            if (paths == null || running)
                return;
            foreach (string path in paths.Where(File.Exists))
            {
                string fullPath = Path.GetFullPath(path);
                if (!selectedFiles.Any(file => string.Equals(file.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
                    selectedFiles.Add(new ConversionRow(fullPath));
            }
            UpdateFileCount();
        }

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            bool canDrop = !running && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
            if (canDrop)
                DropZone.SetResourceReference(Border.BackgroundProperty, "SelectedItemBrush");
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.SetResourceReference(Border.BackgroundProperty, "UploadSurfaceBrush");
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone_DragLeave(sender, e);
            AddFiles(e.Data.GetData(DataFormats.FileDrop) as string[]);
            e.Handled = true;
        }

        private void RemoveFile_Click(object sender, RoutedEventArgs e)
        {
            var file = (ConversionRow)((Button)sender).Tag;
            if (running && file.IsActive) return;
            if (running && !file.HasOutput &&
                !file.StatusText.StartsWith(LocalizationManager.Text("Failed")))
                file.WasRemoved = true;
            selectedFiles.Remove(file);
            UpdateFileCount();
        }

        private void ClearFiles_Click(object sender, RoutedEventArgs e)
        {
            if (running) return;
            selectedFiles.Clear();
            OverallProgress.Value = 0;
            BatchStatus.Text = "";
            UpdateFileCount();
        }

        private void OpenOutput_Click(object sender, RoutedEventArgs e)
        {
            string path = (string)((Button)sender).Tag;
            try
            {
                if (Directory.Exists(path))
                    Process.Start("explorer.exe", "\"" + path + "\"");
                else if (File.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                    throw new FileNotFoundException(LocalizationManager.Text("The output could not be found."));
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FileConverter"); }
        }

        private void UpdateFileCount()
        {
            if (OutputLocation != null) OutputLocation.SetSources(selectedFiles.Select(file => file.FilePath));
            FileCountText.Text = string.Format(LocalizationManager.Text("{0} file(s) selected"), selectedFiles.Count);
            ConvertButton.IsEnabled = method.HasValue && selectedFiles.Count > 0 && !running;
            ClearFilesButton.IsEnabled = selectedFiles.Count > 0 && !running;
        }
    }

    public sealed class ConversionRow : INotifyPropertyChanged
    {
        public ConversionRow(string path)
        {
            FilePath = path;
            StatusText = LocalizationManager.Text("Queued");
        }

        public string FilePath { get; private set; }
        public string FileName { get { return Path.GetFileName(FilePath); } }
        public string StatusText { get; private set; }
        public string OutputPath { get; private set; }
        public bool HasOutput { get { return OutputPath != null; } }
        public bool WasRemoved { get; set; }
        private bool isActive;
        public bool IsActive
        {
            get { return isActive; }
            set
            {
                isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRemove)));
            }
        }
        public bool CanRemove { get { return !IsActive; } }
        public bool IsWorking { get; private set; }
        public int Progress { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        internal void SetProgress(int progress)
        {
            Progress = Math.Max(Progress, progress);
            IsWorking = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        internal void Update(string status, bool working, int progress, string output = null)
        {
            StatusText = status;
            IsWorking = working;
            Progress = progress;
            OutputPath = output;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
