using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileConverter.Services;

namespace FileConverter
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ThemeManager.Initialize();
            LocalizationManager.Initialize();

            EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(ScrollAtBoundary), true);
            base.OnStartup(e);
        }

        private static DependencyObject ParentOf(DependencyObject value)
        {
            var content = value as FrameworkContentElement;
            return content != null ? content.Parent : VisualTreeHelper.GetParent(value);
        }

        private static void ScrollAtBoundary(object sender, MouseWheelEventArgs e)
        {
            // Let the innermost viewer scroll first. At its boundary, pass the
            // wheel to its outer viewer instead of trapping it in a file queue.
            var viewer = (ScrollViewer)sender;
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null && !(current is ScrollViewer)) current = ParentOf(current);
            if (current != viewer || viewer.ScrollableHeight > 0 &&
                (e.Delta > 0 && viewer.VerticalOffset > 0 || e.Delta < 0 && viewer.VerticalOffset < viewer.ScrollableHeight)) return;
            current = VisualTreeHelper.GetParent(viewer);
            while (current != null && (!(current is ScrollViewer) || ((ScrollViewer)current).ScrollableHeight <= 0)) current = ParentOf(current);
            var parent = current as ScrollViewer;
            if (parent == null) return;
            parent.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            OperationSession.CancelAll();
            DesktopConversionService.StopAllWorkers();
            LocalTranslationServer.Stop();
            base.OnExit(e);
        }
    }
}
