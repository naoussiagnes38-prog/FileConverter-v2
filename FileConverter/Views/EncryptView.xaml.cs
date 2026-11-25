
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.IO.Compression;
using FileConverter.Services;
using FileConverter.Core.Services;

namespace FileConverter.Views
{
    public partial class EncryptView : UserControl
    {
        public class EncryptionFile : FileProgressState
        {
            public string FileName { get; set; }
            public string FilePath { get; set; }
        }

        private ObservableCollection<EncryptionFile> selectedFiles;
        private HashSet<string> selectedFilePaths;

        private string outputFolder = string.Empty;

        public EncryptView()
        {
            InitializeComponent();

            selectedFiles = new ObservableCollection<EncryptionFile>();

            selectedFilePaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            FileList.ItemsSource = selectedFiles;

            SameFolderRadio.Checked += OutputLocation_Checked;
            CustomFolderRadio.Checked += OutputLocation_Checked;

            UpdateOutputLocationInterface();
            UpdateInterface();
        }

        private void ResponsiveLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            EncryptionContent.Margin = e.NewSize.Width < 620
                ? new Thickness(12)
                : new Thickness(30);
        }

        private void CancelOperation_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this))
                EncryptionStatusText.Text = LocalizationManager.Text("Cancelling...");
            OperationSession.Cancel(this);
        }

        // Browse and select files
        private void ChooseFiles_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            OpenFileDialog dialog = new OpenFileDialog();

            dialog.Title = "Select files to encrypt or decrypt";
            dialog.Filter = "All files (*.*)|*.*";
            dialog.Multiselect = true;

            if (dialog.ShowDialog() == true)
            {
                AddFiles(dialog.FileNames);
            }
        }

        // Add selected files
        private void AddFiles(string[] filePaths)
        {
            if (OperationSession.GetIsBusy(this)) return;
            int duplicates = 0;

            foreach (string filePath in filePaths)
            {
                if (!File.Exists(filePath))
                    continue;

                string fullPath = Path.GetFullPath(filePath);

                if (selectedFilePaths.Contains(fullPath))
                {
                    duplicates++;
                    continue;
                }

                EncryptionFile file = new EncryptionFile();

                file.FileName = Path.GetFileName(fullPath);
                file.FilePath = fullPath;

                selectedFiles.Add(file);
                selectedFilePaths.Add(fullPath);
            }

            UpdateInterface();
            UpdateOutputLocationInterface();

            if (duplicates > 0)
            {
                MessageBox.Show(
                    duplicates + " file(s) were already selected.",
                    "Duplicate Files",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // Drag over upload area
        private void DropZone_DragOver(object sender, DragEventArgs e)
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

        // Drop files
        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files =
                    (string[])e.Data.GetData(DataFormats.FileDrop);

                AddFiles(files);
            }
        }

        // Remove individual file
        private void RemoveFile_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;

            if (button == null)
                return;

            string filePath = button.Tag as string;

            if (string.IsNullOrEmpty(filePath))
                return;

            EncryptionFile file = selectedFiles.FirstOrDefault(
                f => string.Equals(
                    f.FilePath,
                    filePath,
                    StringComparison.OrdinalIgnoreCase));

            if (file != null)
            {
                if (OperationSession.GetIsBusy(this) &&
                    file.StatusText == LocalizationManager.Text("Working")) return;
                selectedFiles.Remove(file);
                selectedFilePaths.Remove(filePath);
            }

            UpdateInterface();
        }

        // Clear all selected files
        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            if (selectedFiles.Count == 0)
                return;

            MessageBoxResult result = MessageBox.Show(
                "Are you sure you want to remove all selected files?",
                "Clear Selected Files",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();

                UpdateInterface();
            }
        }

        // Update file selection interface
        private void UpdateInterface()
        {
            UpdateOutputLocationInterface();
            int count = selectedFiles.Count;

            FileCountText.Text = string.Format(LocalizationManager.Text("{0} file(s) selected"), count);

            EmptyListText.Visibility =
                count == 0 ? Visibility.Visible : Visibility.Collapsed;

            ClearAllButton.IsEnabled = count > 0;

            if (count == 0)
            {
                EncryptionStatusText.Text = LocalizationManager.Text("Ready.");
            }
            else
            {
                EncryptionStatusText.Text =
                    string.Format(LocalizationManager.Text("{0} file(s) ready."), count);
            }
        }

        // Output folder selection
        private void OutputLocation_Checked(
            object sender, RoutedEventArgs e)
        {
            UpdateOutputLocationInterface();
        }

        private void UpdateOutputLocationInterface()
        {
            bool customFolder =
                CustomFolderRadio.IsChecked == true;

            BrowseFolderButton.IsEnabled = customFolder;


            if (customFolder)
            {
                OutputFolderTextBox.Text = string.IsNullOrWhiteSpace(outputFolder) ? LocalizationManager.Text("No destination selected") : outputFolder;
            }
            else
            {
                OutputFolderTextBox.Text =
                    selectedFiles.Count > 0 ? Path.GetDirectoryName(selectedFiles[0].FilePath) : LocalizationManager.Text("Original file locations");
            }
        }

        // Browse output folder
        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            using (System.Windows.Forms.FolderBrowserDialog dialog =
                new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select the output folder";

                if (dialog.ShowDialog() ==
                    System.Windows.Forms.DialogResult.OK)
                {
                    outputFolder = dialog.SelectedPath;
                    OutputFolderTextBox.Text = string.IsNullOrWhiteSpace(outputFolder) ? LocalizationManager.Text("No destination selected") : outputFolder;
                }
            }
        }

        private async void EncryptButton_Click(object sender, RoutedEventArgs e)
        {
            await ProcessFilesAsync(false);
        }

        private async void DecryptButton_Click(object sender, RoutedEventArgs e)
        {
            await ProcessFilesAsync(true);
        }

        private async Task ProcessFilesAsync(bool decrypt)
        {
            if (OperationSession.GetIsBusy(this)) return;
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("Select at least one file.", "FileConverter");
                return;
            }

            string password = EncryptionPasswordBox.Password;
            if (string.IsNullOrEmpty(password) ||
                (!decrypt && (password.Length < 8 || password != ConfirmPasswordBox.Password)))
            {
                MessageBox.Show(decrypt ? "Enter the original password." :
                    "Enter and confirm a password of at least eight characters.", "FileConverter");
                return;
            }

            bool custom = CustomFolderRadio.IsChecked == true;
            if (custom && !Directory.Exists(outputFolder))
            {
                MessageBox.Show("Select a valid destination folder.", "FileConverter");
                return;
            }

            var files = selectedFiles.Select(file => file.FilePath).ToArray();
            var operationFiles = files;
            string folder = custom ? outputFolder : Path.GetDirectoryName(files[0]);
            bool together = !decrypt && SingleEncryptedArchive.IsChecked == true;
            var service = new FileEncryptionService();
            var rows = selectedFiles.ToDictionary(file => file.FilePath, StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Values) row.UpdateProgress(0, LocalizationManager.Text("Queued"));
            var source = OperationSession.Start(this);
            EncryptionStatusText.Text = LocalizationManager.Text("Working");
            try
            {
                await BatchOperations.RunAsync(this, together ? new[] { files[0] } : files,
                    file => Path.Combine(custom || together ? folder : Path.GetDirectoryName(file),
                        together ? "files.zip.encrypted" : decrypt ? DecryptedName(file) : Path.GetFileName(file) + ".encrypted"),
                    (file, output, progress) =>
                    {
                        if (decrypt) service.DecryptFile(file, output, password, progress, source.Token);
                        else if (!together) service.EncryptFile(file, output, password, progress, source.Token);
                        else
                        {
                            string work = Path.Combine(Path.GetTempPath(), "FileConverter-" + Guid.NewGuid().ToString("N") + ".zip");
                            try
                            {
                                new CompressionService().CompressFiles(operationFiles, work, CompressionLevel.Fastest,
                                    fraction => progress(0.3 * fraction), source.Token);
                                service.EncryptFile(work, output, password, fraction => progress(0.3 + 0.7 * fraction), source.Token);
                            }
                            finally { if (File.Exists(work)) File.Delete(work); }
                        }
                    }, percent => OperationProgress.Value = percent, source.Token, false,
                    together ? (Func<string, IEnumerable<string>>)(file => operationFiles) : null,
                    (file, percent, status) =>
                    {
                        if (together) foreach (var row in rows.Values) row.UpdateProgress(percent, status);
                        else rows[file].UpdateProgress(percent, status);
                    }, maxConcurrency: together ? 1 : 2, sourceInterface: "Encrypt",
                    tryStartFile: file =>
                    {
                        if (together)
                        {
                            operationFiles = selectedFiles.Select(item => item.FilePath).ToArray();
                            foreach (var row in rows.Values.Where(selectedFiles.Contains))
                                row.UpdateProgress(0, LocalizationManager.Text("Working"));
                            return operationFiles.Length > 0;
                        }
                        EncryptionFile rowItem;
                        if (!rows.TryGetValue(file, out rowItem) || !selectedFiles.Contains(rowItem)) return false;
                        rowItem.UpdateProgress(0, LocalizationManager.Text("Working"));
                        return true;
                    });
                EncryptionStatusText.Text = LocalizationManager.Text(source.IsCancellationRequested ? "Cancelled" : "Finished");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FileConverter"); }
            finally
            {
                OperationSession.Finish(this);
                selectedFiles.Clear(); selectedFilePaths.Clear();
                OperationProgress.Value = 0;
                EncryptionPasswordBox.Clear(); ConfirmPasswordBox.Clear();
                UpdateInterface();
            }
        }

        private static string DecryptedName(string file)
        {
            string name = Path.GetFileName(file);
            return name.EndsWith(".encrypted", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - ".encrypted".Length) : name + ".decrypted";
        }
    }
}
