using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using System.IO.Compression;
using System.Threading;
using FileConverter.Services;
using FileConverter.Core.Services;


namespace FileConverter.Views
{
    /// <summary>
    /// Logique d'interaction pour CompressView.xaml
    /// </summary>
    public partial class CompressView : UserControl
    {
        // ==========================================
        // FILE MODEL
        // ==========================================

        public class CompressionFile : FileProgressState
        {
            public string FileName { get; set; }

            public string FilePath { get; set; }
            public string FileSize { get; set; }
        }

        // ==========================================
        // FILE COLLECTION
        // ==========================================

        private ObservableCollection<CompressionFile> selectedFiles;

        private HashSet<string> selectedFilePaths;

        // ==========================================
        // COMPRESSION SETTINGS
        // ==========================================

        private string outputFolder = string.Empty;


        // ==========================================
        // CONSTRUCTOR
        // ==========================================

        public CompressView()
        {
            InitializeComponent();

            selectedFiles = new ObservableCollection<CompressionFile>();

            selectedFilePaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            FileList.ItemsSource = selectedFiles;

            // Compression settings events

            SameFolderRadio.Checked += OutputLocation_Checked;

            CustomFolderRadio.Checked += OutputLocation_Checked;



            UpdateOutputLocationInterface();

            UpdateInterface();
        }

        private void ResponsiveLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CompressionContent.Margin = e.NewSize.Width < 620
                ? new Thickness(12)
                : new Thickness(30);
        }

        private void CancelOperation_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this))
                CompressionStatusText.Text = LocalizationManager.Text("Cancelling...");
            OperationSession.Cancel(this);
        }

        // ==========================================
        // CHOOSE FILES
        // ==========================================

        private void ChooseFiles_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            OpenFileDialog dialog = new OpenFileDialog();

            dialog.Title = "Select Files to Compress";

            dialog.Multiselect = true;

            dialog.CheckFileExists = true;

            dialog.CheckPathExists = true;

            dialog.Filter = ArchiveOperation.SelectedIndex == 1
                ? "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*"
                : "All Files (*.*)|*.*";

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                AddFiles(dialog.FileNames);
            }
        }

        private void ArchiveOperation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CompressionDescription == null || ArchiveOperation == null) return;
            CompressionDescription.SetResourceReference(TextBlock.TextProperty,
                ArchiveOperation.SelectedIndex == 1
                    ? "L_Extract_ZIP_archives_into_a_folder_PowerPoint_presentations_should_be_opened_or_converted_instead"
                    : "L_Create_a_separate_ZIP_archive_for_each_selected_file");
        }

        // ==========================================
        // ADD FILES
        // ==========================================

        private void AddFiles(string[] filePaths)
        {
            if (OperationSession.GetIsBusy(this)) return;
            if (filePaths == null)
            {
                return;
            }

            int duplicateCount = 0;

            foreach (string filePath in filePaths)
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    continue;
                }

                if (!File.Exists(filePath))
                {
                    continue;
                }

                string fullPath = System.IO.Path.GetFullPath(filePath);

                if (selectedFilePaths.Contains(fullPath))
                {
                    duplicateCount++;

                    continue;
                }

                CompressionFile file = new CompressionFile();

                file.FileName = System.IO.Path.GetFileName(fullPath);

                file.FilePath = fullPath;
                file.FileSize = new FileInfo(fullPath).Length.ToString("N0") + " bytes";

                selectedFiles.Add(file);

                selectedFilePaths.Add(fullPath);
            }

            UpdateInterface();
            UpdateOutputLocationInterface();

            if (duplicateCount > 0)
            {
                System.Windows.MessageBox.Show(
                    duplicateCount +
                    " file(s) were already in the selection.",
                    "Duplicate Files",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // ==========================================
        // DRAG OVER
        // ==========================================

        private void DropZone_DragOver(
            object sender,
            System.Windows.DragEventArgs e)
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

        // ==========================================
        // DROP FILES
        // ==========================================

        private void DropZone_Drop(
            object sender,
            System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] droppedFiles =
                e.Data.GetData(DataFormats.FileDrop) as string[];

            AddFiles(droppedFiles);
        }

        // ==========================================
        // REMOVE INDIVIDUAL FILE
        // ==========================================

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

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            CompressionFile file = selectedFiles.FirstOrDefault(
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

        // ==========================================
        // CLEAR ALL FILES
        // ==========================================

        private void ClearAll_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            if (selectedFiles.Count == 0)
            {
                return;
            }

            MessageBoxResult result = System.Windows.MessageBox.Show(
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

        // ==========================================
        // UPDATE FILE INTERFACE
        // ==========================================

        private void UpdateInterface()
        {
            UpdateOutputLocationInterface();
            int count = selectedFiles.Count;

            FileCountText.Text = count.ToString();

            if (count == 0)
            {
                EmptyListText.Visibility = Visibility.Visible;

                ClearAllButton.IsEnabled = false;

                CompressionStatusText.Text = LocalizationManager.Text("Not configured");
            }
            else
            {
                EmptyListText.Visibility = Visibility.Collapsed;

                ClearAllButton.IsEnabled = true;

                CompressionStatusText.Text = LocalizationManager.Text("Files selected");
            }
        }

        // ==========================================
        // COMPRESSION LEVEL
        // ==========================================


        // ==========================================
        // OUTPUT LOCATION
        // ==========================================

        private void OutputLocation_Checked(
            object sender,
            RoutedEventArgs e)
        {
            UpdateOutputLocationInterface();
        }

        // ==========================================
        // UPDATE OUTPUT LOCATION INTERFACE
        // ==========================================

        private void UpdateOutputLocationInterface()
        {
            if (SameFolderRadio.IsChecked == true)
            {
                BrowseFolderButton.IsEnabled = false;

                OutputFolderTextBox.Text =
                    selectedFiles.Count > 0 ? Path.GetDirectoryName(selectedFiles[0].FilePath) : LocalizationManager.Text("Same folder as original files");


            }
            else if (CustomFolderRadio.IsChecked == true)
            {
                BrowseFolderButton.IsEnabled = true;

                if (string.IsNullOrWhiteSpace(outputFolder))
                {
                    OutputFolderTextBox.Text =
                        LocalizationManager.Text("No destination selected");
                }
                else
                {
                    OutputFolderTextBox.Text = outputFolder;
                }
            }
        }

        // ==========================================
        // BROWSE OUTPUT FOLDER
        // ==========================================

        private void BrowseFolder_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            using (System.Windows.Forms.FolderBrowserDialog dialog =
                new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description =
                    "Select the destination folder for compressed files.";

                dialog.ShowNewFolderButton = true;

                System.Windows.Forms.DialogResult result =
                    dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK &&
                    !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    outputFolder = dialog.SelectedPath;

                    OutputFolderTextBox.Text = outputFolder;
                }
            }
        }

        // ==========================================
        // KEEP ORIGINAL FILES
        // ==========================================

        private void SummaryColumns_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var grid = (Grid)sender;
            if (grid.ColumnDefinitions.Count != 3 || grid.Children.Count < 2) return;
            bool narrow = grid.ActualWidth < 650;
            grid.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 12);
            grid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(grid.Children[1], narrow ? 0 : 2);
            Grid.SetRow(grid.Children[1], narrow ? 1 : 0);
            ((FrameworkElement)grid.Children[1]).Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        }

        private async void CompressFiles_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            bool custom = CustomFolderRadio.IsChecked == true;
            if (selectedFiles.Count == 0 || (custom && !Directory.Exists(outputFolder)))
            {
                MessageBox.Show(LocalizationManager.Text("Select files and a valid destination folder."), "FileConverter");
                return;
            }
            var files = selectedFiles.Select(file => file.FilePath).ToArray();
            var operationFiles = files;
            string folder = custom ? outputFolder : Path.GetDirectoryName(files[0]);
            int operation = ArchiveOperation.SelectedIndex;
            if (operation == 1 && files.Any(file => !Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(LocalizationManager.Text("Decompression accepts ZIP archives only. A PowerPoint PPTX is already a presentation package; open it in PowerPoint or use Convert instead of extracting its internal parts."),
                    LocalizationManager.Text("Decompress"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            bool together = files.Length > 1 && operation == 0;
            var level = CompressionLevel.Fastest;
            var rows = selectedFiles.ToDictionary(file => file.FilePath, StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Values) row.UpdateProgress(0, LocalizationManager.Text("Queued"));
            var source = OperationSession.Start(this);
            CompressionStatusText.Text = LocalizationManager.Text("Working");
            var service = new CompressionService();
            try
            {
                await BatchOperations.RunAsync(this, together ? new[] { files[0] } : files,
                    file => Path.Combine(custom || together ? folder : Path.GetDirectoryName(file),
                        together ? "archive.zip" : Path.GetFileNameWithoutExtension(file) +
                            (operation == 1 ? ".extracted" : ".zip")),
                    (file, output, progress) =>
                    {
                        if (operation == 1) service.Decompress(file, output, progress, source.Token);
                        else service.CompressFiles(together ? operationFiles : new[] { file }, output, level, progress, source.Token);
                    }, percent => OperationProgress.Value = percent, source.Token, operation == 1,
                    together ? (Func<string, IEnumerable<string>>)(file => operationFiles) : null,
                    (file, percent, status) =>
                    {
                        if (together) foreach (var row in rows.Values) row.UpdateProgress(percent, status);
                        else rows[file].UpdateProgress(percent, status);
                    }, maxConcurrency: together ? 1 : 2, sourceInterface: "Compress",
                    tryStartFile: file =>
                    {
                        if (together)
                        {
                            operationFiles = selectedFiles.Select(item => item.FilePath).ToArray();
                            foreach (var row in rows.Values.Where(selectedFiles.Contains))
                                row.UpdateProgress(0, LocalizationManager.Text("Working"));
                            return operationFiles.Length > 0;
                        }
                        CompressionFile rowItem;
                        if (!rows.TryGetValue(file, out rowItem) || !selectedFiles.Contains(rowItem)) return false;
                        rowItem.UpdateProgress(0, LocalizationManager.Text("Working"));
                        return true;
                    });
                CompressionStatusText.Text = LocalizationManager.Text(source.IsCancellationRequested ? "Cancelled" : "Finished");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FileConverter"); }
            finally
            {
                OperationSession.Finish(this);
                selectedFiles.Clear(); selectedFilePaths.Clear();
                OperationProgress.Value = 0;

                UpdateInterface();
            }
        }
    }
}
