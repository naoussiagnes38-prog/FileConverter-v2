using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;

namespace FileConverter.Services
{
    public static class OperationSession
    {
        public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached(
            "IsBusy", typeof(bool), typeof(OperationSession),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
        private static readonly Dictionary<DependencyObject, CancellationTokenSource> Sessions =
            new Dictionary<DependencyObject, CancellationTokenSource>();
        public static bool GetIsBusy(DependencyObject view) { return (bool)view.GetValue(IsBusyProperty); }
        public static void SetIsBusy(DependencyObject view, bool value) { view.SetValue(IsBusyProperty, value); }
        public static CancellationTokenSource Start(DependencyObject view)
        {
            if (GetIsBusy(view)) throw new InvalidOperationException("An operation is already running.");
            var source = new CancellationTokenSource();
            Sessions.Add(view, source);
            SetIsBusy(view, true);
            return source;
        }
        public static bool IsCurrent(DependencyObject view, CancellationTokenSource source)
        {
            CancellationTokenSource current;
            return Sessions.TryGetValue(view, out current) && ReferenceEquals(current, source);
        }
        public static bool Cancel(DependencyObject view)
        {
            if (view == null) return false;
            CancellationTokenSource source;
            if (!Sessions.TryGetValue(view, out source)) return false;
            try
            {
                if (source.IsCancellationRequested) return false;
                source.Cancel();
                return true;
            }
            catch (ObjectDisposedException) { return false; }
            catch (AggregateException) { return true; }
        }
        public static void Finish(DependencyObject view)
        {
            CancellationTokenSource source;
            if (Sessions.TryGetValue(view, out source)) { Sessions.Remove(view); source.Dispose(); }
            SetIsBusy(view, false);
        }
        public static void CancelAll() { foreach (var source in Sessions.Values.ToArray()) source.Cancel(); }
    }
}
