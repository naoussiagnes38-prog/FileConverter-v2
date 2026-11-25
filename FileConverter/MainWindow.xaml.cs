
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FileConverter.Views;
using FileConverter.Services;

namespace FileConverter
{
    public partial class MainWindow : Window
    {
        private ConvertView conversionView;
        private TranslateView translationView;
        private CompressView compressionView;
        private EncryptView encryptionView;
        private bool sidebarExpanded = true;
        private bool compactLayout;
        private string currentTitle;

        public MainWindow()
        {
            InitializeComponent();
            Rect workArea = SystemParameters.WorkArea;
            if (workArea.Height > workArea.Width)
            {
                Width = Math.Max(MinWidth, Math.Min(700, workArea.Width * 0.9));
                Height = Math.Max(MinHeight, Math.Min(900, workArea.Height * 0.9));
            }
            else
            {
                Width = Math.Min(Width, workArea.Width);
                Height = Math.Min(Height, workArea.Height);
            }
            SidebarVersion.Text = "FileConverter v" + typeof(MainWindow).Assembly.GetName().Version;
            SizeChanged += (sender, args) => UpdateResponsiveLayout();
            Closing += (sender, args) => OperationSession.CancelAll();
            UpdateResponsiveLayout();

            NavigateTo(
                conversionView ?? (conversionView = new ConvertView()),
                "Convert",
                ConvertButton);
        }

        private void UpdateResponsiveLayout()
        {
            bool useCompactLayout = ActualWidth < 850 || ActualHeight > ActualWidth * 1.08;
            if (compactLayout == useCompactLayout && LayoutRoot.ActualWidth > 0)
                return;

            compactLayout = useCompactLayout;
            if (compactLayout)
            {
                SidebarColumn.Width = new GridLength(0);
                SidebarBorder.SetValue(Grid.RowProperty, 0);
                SidebarBorder.SetValue(Grid.ColumnProperty, 0);
                SidebarBorder.SetValue(Grid.RowSpanProperty, 1);
                SidebarBorder.SetValue(Grid.ColumnSpanProperty, 2);
                SidebarBorder.BorderThickness = new Thickness(0, 0, 0, 1);
                SidebarBorder.Height = 100;
                SidebarHeader.Orientation = Orientation.Horizontal;
                SidebarHeader.Margin = new Thickness(8, 4, 4, 4);
                SidebarHeader.VerticalAlignment = VerticalAlignment.Center;
                SidebarLogo.Margin = new Thickness(4, 0, 8, 0);
                SidebarToggleButton.Visibility = Visibility.Collapsed;
                NavigationScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                NavigationScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                NavigationPanel.Orientation = Orientation.Horizontal;
                MainContentGrid.SetValue(Grid.RowProperty, 1);
                MainContentGrid.SetValue(Grid.ColumnProperty, 0);
                MainContentGrid.SetValue(Grid.RowSpanProperty, 1);
                MainContentGrid.SetValue(Grid.ColumnSpanProperty, 2);
                TopBarRow.Height = new GridLength(58);
                TopBarContent.Margin = new Thickness(12, 0, 12, 0);
                PageHost.Margin = new Thickness(6);
            }
            else
            {
                SidebarColumn.Width = sidebarExpanded ? new GridLength(245) : new GridLength(75);
                SidebarBorder.SetValue(Grid.RowProperty, 0);
                SidebarBorder.SetValue(Grid.ColumnProperty, 0);
                SidebarBorder.SetValue(Grid.RowSpanProperty, 2);
                SidebarBorder.SetValue(Grid.ColumnSpanProperty, 1);
                SidebarBorder.BorderThickness = new Thickness(0, 0, 1, 0);
                SidebarBorder.ClearValue(HeightProperty);
                SidebarHeader.Orientation = Orientation.Vertical;
                SidebarHeader.Margin = new Thickness(15, 20, 15, 20);
                SidebarHeader.VerticalAlignment = VerticalAlignment.Top;
                SidebarToggleButton.Visibility = Visibility.Visible;
                NavigationScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                NavigationScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                NavigationPanel.Orientation = Orientation.Vertical;
                MainContentGrid.SetValue(Grid.RowProperty, 0);
                MainContentGrid.SetValue(Grid.ColumnProperty, 1);
                MainContentGrid.SetValue(Grid.RowSpanProperty, 2);
                MainContentGrid.SetValue(Grid.ColumnSpanProperty, 1);
                TopBarRow.Height = new GridLength(75);
                TopBarContent.Margin = new Thickness(20, 0, 20, 0);
                PageHost.Margin = new Thickness(12);
            }
            UpdateSidebarContent();
        }

        // Navigate between application screens
        private void NavigateTo(
            UserControl view,
            string title,
            Button selectedButton)
        {
            PageHost.Content = view;
            currentTitle = title;
            RefreshLanguage();

            Button[] buttons =
            {
                ConvertButton,
                TranslateButton,
                HistoryButton,
                ManualButton,
                CompressButton,
                EncryptButton
            };

            foreach (Button button in buttons)
            {
                button.SetResourceReference(
                    Control.BackgroundProperty,
                    "SidebarBackgroundBrush");

                button.SetResourceReference(
                    Control.ForegroundProperty,
                    "PrimaryTextBrush");
            }

            if (selectedButton != null)
            {
                selectedButton.SetResourceReference(
                    Control.BackgroundProperty,
                    "SelectedItemBrush");

                selectedButton.SetResourceReference(
                    Control.ForegroundProperty,
                    "PrimaryTextBrush");
            }
        }

        // Expand or collapse sidebar
        private void ToggleSidebar_Click(
            object sender,
            RoutedEventArgs e)
        {
            sidebarExpanded = !sidebarExpanded;

            if (!compactLayout)
                SidebarColumn.Width = sidebarExpanded ? new GridLength(245) : new GridLength(75);

            UpdateSidebarContent();
        }

        internal void RefreshLanguage()
        {
            if (currentTitle == null)
                return;
            PageTitle.Text = LocalizationManager.Text(currentTitle);
            UpdateSidebarContent();
        }

        // Update sidebar according to its state
        private void UpdateSidebarContent()
        {
            ConvertButton.ToolTip = LocalizationManager.Text("Convert");
            TranslateButton.ToolTip = LocalizationManager.Text("Translate");
            CompressButton.ToolTip = LocalizationManager.Text("Compress");
            EncryptButton.ToolTip = LocalizationManager.Text("Encrypt");
            ManualButton.ToolTip = LocalizationManager.Text("Manual");
            HistoryButton.ToolTip = LocalizationManager.Text("History");
            bool showLabels = sidebarExpanded && !compactLayout;
            SidebarName.Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed;
            SidebarLogo.Visibility = showLabels ? Visibility.Collapsed : Visibility.Visible;
            SidebarVersion.Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed;
            if (showLabels)
            {
                ConvertButton.Content = "⇄    " + LocalizationManager.Text("Convert");
                TranslateButton.Content = "文    " + LocalizationManager.Text("Translate");
                HistoryButton.Content = "◷    " + LocalizationManager.Text("History");
                ManualButton.Content = "?    " + LocalizationManager.Text("Manual");
                CompressButton.Content = "▣    " + LocalizationManager.Text("Compress");
                EncryptButton.Content = "◆    " + LocalizationManager.Text("Encrypt");

                ConvertButton.HorizontalContentAlignment = HorizontalAlignment.Left;
                TranslateButton.HorizontalContentAlignment = HorizontalAlignment.Left;
                HistoryButton.HorizontalContentAlignment = HorizontalAlignment.Left;
                ManualButton.HorizontalContentAlignment = HorizontalAlignment.Left;
                CompressButton.HorizontalContentAlignment = HorizontalAlignment.Left;
                EncryptButton.HorizontalContentAlignment = HorizontalAlignment.Left;
                SetNavigationButtonSize(double.NaN, 48, new Thickness(10, 4, 10, 4));
            }
            else
            {
                ConvertButton.Content = "⇄";
                TranslateButton.Content = "文";
                HistoryButton.Content = "◷";
                ManualButton.Content = "?";
                CompressButton.Content = "▣";
                EncryptButton.Content = "◆";

                ConvertButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                TranslateButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                HistoryButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                ManualButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                CompressButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                EncryptButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                SetNavigationButtonSize(compactLayout ? 48 : 58, compactLayout ? 42 : 48,
                    compactLayout ? new Thickness(1, 3, 1, 3) : new Thickness(8, 4, 8, 4));
            }
        }

        private void SetNavigationButtonSize(double width, double height, Thickness margin)
        {
            Button[] buttons = { ConvertButton, TranslateButton, CompressButton, EncryptButton, ManualButton, HistoryButton };
            foreach (Button button in buttons)
            {
                button.Width = width;
                button.Height = height;
                button.Margin = margin;
                button.Padding = compactLayout ? new Thickness(4, 0, 4, 0) : new Thickness(15, 0, 15, 0);
            }
        }

        // Convert page
        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(
                conversionView ?? (conversionView = new ConvertView()),
                "Convert",
                ConvertButton);
        }

        // Translation page
        private void Translate_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(
                translationView ?? (translationView = new TranslateView()),
                "Translate",
                TranslateButton);
        }

        // History page
        private void History_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(
                new HistoryView(),
                "History",
                HistoryButton);
        }

        // Manual page
        private void Manual_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(
                new ManualView(),
                "Manual",
                ManualButton);
        }

        // Compression page
        private void Compress_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(
                compressionView ?? (compressionView = new CompressView()),
                "Compress",
                CompressButton);
        }

        // Encryption page
        private void Encrypt_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(
                encryptionView ?? (encryptionView = new EncryptView()),
                "Encrypt",
                EncryptButton);
        }

        // Settings page
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(
                new SettingsView(),
                "Settings",
                null);
        }
    }
}
