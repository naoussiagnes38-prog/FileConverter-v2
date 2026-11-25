using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FileConverter.Services;
using Forms = System.Windows.Forms;

namespace FileConverter.Controls
{
    public partial class OutputLocationPicker : UserControl
    {
        private string[] sourceFolders = new string[0];
        private string customPath = "";

        public OutputLocationPicker()
        {
            InitializeComponent();
            RefreshLocation();
        }

        public bool TryGetFolder(out string folder)
        {
            folder = CustomFolder.IsChecked == true ? FolderPath.Text : null;
            if (folder == null || Directory.Exists(folder))
                return true;

            MessageBox.Show(LocalizationManager.Text("Choose an existing output folder."),
                LocalizationManager.Text("Output Location"), MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void Location_Changed(object sender, RoutedEventArgs e) { if (FolderPath != null) RefreshLocation(); }
        public void SetSources(IEnumerable<string> files)
        {
            sourceFolders = files.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            RefreshLocation();
        }
        private void RefreshLocation()
        {
            if (OriginalFolder.IsChecked == true)
            {
                FolderPath.Text = sourceFolders.Length > 0 ? sourceFolders[0] : LocalizationManager.Text("Same folder as each source file");
                FolderPath.ToolTip = string.Join(Environment.NewLine, sourceFolders);
            }
            else { FolderPath.Text = customPath; FolderPath.ToolTip = customPath; }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (OperationSession.GetIsBusy(this)) return;
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = LocalizationManager.Text("Choose an output folder.");
                if (Directory.Exists(FolderPath.Text))
                    dialog.SelectedPath = FolderPath.Text;
                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    customPath = dialog.SelectedPath;
                    RefreshLocation();
                }
            }
        }
    }
}
