using System;
using System.ComponentModel;

namespace FileConverter.Services
{
    public class FileProgressState : INotifyPropertyChanged
    {
        public int ProgressPercent { get; private set; }
        public string StatusText { get; private set; } = LocalizationManager.Text("Queued");
        public bool IsWorking { get; private set; }
        public bool CanRemove { get { return !IsWorking; } }
        public event PropertyChangedEventHandler PropertyChanged;
        public void UpdateProgress(double percent, string status)
        {
            ProgressPercent = Math.Max(0, Math.Min(100, (int)percent));
            StatusText = status;
            IsWorking = status == LocalizationManager.Text("Working");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
