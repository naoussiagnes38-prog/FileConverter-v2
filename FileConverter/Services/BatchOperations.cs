using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using FileConverter.Views;

namespace FileConverter.Services
{
    internal static class BatchOperations
    {
        private static readonly object OutputNameLock = new object();

        public static async Task RunAsync(UIElement view, IEnumerable<string> files,
            Func<string, string> destination, Action<string, string, Action<double>> operation,
            Action<double> reportProgress, CancellationToken token = default(CancellationToken),
            bool directories = false, Func<string, IEnumerable<string>> historyInputs = null,
            Action<string, double, string> fileProgress = null, int maxConcurrency = 1,
            string sourceInterface = "Convert", Func<string, bool> tryStartFile = null)
        {
            var errors = new List<string>();
            var renamed = new List<string>();
            int completed = 0;
            var queue = files.ToArray();
            if (queue.Length == 0) return;
            double[] weights = queue.Select(WorkWeight).ToArray();
            double totalWeight = Math.Max(1, weights.Sum());
            var fractions = new double[queue.Length];
            var overallComplete = new bool[queue.Length];
            var statuses = queue.Select(_ => LocalizationManager.Text("Queued")).ToArray();
            var lastFilePercent = Enumerable.Repeat(-1, queue.Length).ToArray();
            var lastOverallPercent = -1;
            var progressLock = new object();

            Action<int, double, string, bool, bool> updateProgress = (index, fraction, status, finished, force) =>
            {
                int filePercent;
                int overallPercent;
                bool changed;
                lock (progressLock)
                {
                    fractions[index] = Math.Max(fractions[index], Math.Max(0, Math.Min(fraction, 1)));
                    statuses[index] = status;
                    if (finished) overallComplete[index] = true;
                    filePercent = (int)Math.Floor(fractions[index] * 100);
                    double weighted = 0;
                    for (int item = 0; item < queue.Length; item++)
                        weighted += weights[item] * (overallComplete[item] ? 1 : fractions[item]);
                    overallPercent = (int)Math.Floor(100 * weighted / totalWeight);
                    changed = force || filePercent != lastFilePercent[index] || overallPercent != lastOverallPercent;
                    if (!changed) return;
                    lastFilePercent[index] = filePercent;
                    lastOverallPercent = overallPercent;
                }

                Action publish = () =>
                {
                    int currentFilePercent;
                    int currentOverallPercent;
                    string currentStatus;
                    lock (progressLock)
                    {
                        currentFilePercent = (int)Math.Floor(fractions[index] * 100);
                        double weighted = 0;
                        for (int item = 0; item < queue.Length; item++)
                            weighted += weights[item] * (overallComplete[item] ? 1 : fractions[item]);
                        currentOverallPercent = (int)Math.Floor(100 * weighted / totalWeight);
                        currentStatus = statuses[index];
                    }
                    reportProgress(currentOverallPercent);
                    fileProgress?.Invoke(queue[index], currentFilePercent, currentStatus);
                };

                if (view.Dispatcher.CheckAccess()) publish();
                else if (!view.Dispatcher.HasShutdownStarted && !view.Dispatcher.HasShutdownFinished)
                    view.Dispatcher.BeginInvoke(publish);
            };

            reportProgress(0);
            using (var workers = new SemaphoreSlim(Math.Max(1, Math.Min(maxConcurrency, queue.Length))))
            {
                var tasks = queue.Select((file, index) => Task.Run(async () =>
                {
                    bool acquired = false;
                    double currentFraction = 0;
                    string output = null;
                    string finalStatus = "Failed";
                    try
                    {
                        await workers.WaitAsync(token);
                        acquired = true;
                        token.ThrowIfCancellationRequested();
                        if (tryStartFile != null)
                        {
                            bool canStart = false;
                            Action start = () => canStart = tryStartFile(file);
                            if (view.Dispatcher.CheckAccess()) start();
                            else view.Dispatcher.Invoke(start);
                            if (!canStart)
                            {
                                updateProgress(index, 1, LocalizationManager.Text("Removed"), true, true);
                                return;
                            }
                        }
                        updateProgress(index, currentFraction, LocalizationManager.Text("Working"), false, true);
                        output = destination(file);
                        string requested = output;
                        output = await Task.Run(() => WriteOutput(requested, temp => operation(file, temp, fraction =>
                        {
                            token.ThrowIfCancellationRequested();
                            currentFraction = Math.Max(currentFraction, Math.Min(fraction, 0.99));
                            updateProgress(index, currentFraction, LocalizationManager.Text("Working"), false, false);
                        }), directories, token));
                        finalStatus = "Completed";
                        lock (renamed) renamed.Add(output);
                        Interlocked.Increment(ref completed);
                        updateProgress(index, 1, LocalizationManager.Text("Done"), true, true);
                    }
                    catch (OperationCanceledException)
                    {
                        finalStatus = "Cancelled";
                        updateProgress(index, currentFraction, LocalizationManager.Text("Cancelled"), false, true);
                    }
                    catch (Exception ex)
                    {
                        lock (errors) errors.Add(Path.GetFileName(file) + ": " + ex.Message);
                        updateProgress(index, currentFraction, LocalizationManager.Text("Failed") + " · " + ex.Message, true, true);
                    }
                    finally
                    {
                        if (acquired) workers.Release();
                    }

                    Action recordHistory = () =>
                    {
                        foreach (string source in historyInputs == null ? new[] { file } : historyInputs(file))
                            HistoryView.Record(source, output, finalStatus, sourceInterface);
                    };
                    if (view.Dispatcher.CheckAccess()) recordHistory();
                    else if (!view.Dispatcher.HasShutdownStarted && !view.Dispatcher.HasShutdownFinished)
                        view.Dispatcher.Invoke(recordHistory);
                })).ToArray();

                await Task.WhenAll(tasks);
            }

            string[] completedOutputs;
            string[] failures;
            lock (renamed) completedOutputs = renamed.ToArray();
            lock (errors) failures = errors.ToArray();
            MessageBox.Show((token.IsCancellationRequested ? LocalizationManager.Text("Cancelled") + "\n" : "") + string.Format(LocalizationManager.Text("{0} completed, {1} failed."), completed, failures.Length) + "\n" +
                string.Join("\n", completedOutputs.Take(5)) + (completedOutputs.Length > 0 && failures.Length > 0 ? "\n" : "") +
                string.Join("\n", failures.Take(5)), "FileConverter");
        }

        private static double WorkWeight(string path)
        {
            try { return Math.Max(1.0, new FileInfo(path).Length); }
            catch (IOException) { return 1; }
            catch (UnauthorizedAccessException) { return 1; }
        }

        public static string WriteOutput(string output, Action<string> write, bool isDirectory = false, CancellationToken token = default(CancellationToken))
        {
            output = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(output);
            string name = isDirectory ? Path.GetFileName(output) : Path.GetFileNameWithoutExtension(output);
            string extension = isDirectory ? "" : Path.GetExtension(output);
            if (extension.Equals(".encrypted", StringComparison.OrdinalIgnoreCase))
            {
                // Keep the original extension intact when the encrypted suffix is removed.
                extension = Path.GetExtension(name) + extension;
                name = Path.GetFileNameWithoutExtension(name);
            }
            string temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp" + extension);
            try
            {
                if (isDirectory)
                    Directory.CreateDirectory(temp);
                token.ThrowIfCancellationRequested();
                write(temp);
                token.ThrowIfCancellationRequested();
                lock (OutputNameLock)
                {
                    for (int copy = 1; ; copy++)
                    {
                        token.ThrowIfCancellationRequested();
                        string candidate = copy == 1 ? output : Path.Combine(directory,
                            name + " (" + copy.ToString(CultureInfo.InvariantCulture) + ")" + extension);
                        if (File.Exists(candidate) || Directory.Exists(candidate))
                            continue;

                        try
                        {
                            if (isDirectory)
                                Directory.Move(temp, candidate);
                            else
                                File.Move(temp, candidate);
                            return candidate;
                        }
                        catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
                        {
                            // Another operation took the name before the move.
                        }
                    }
                }
            }
            finally
            {
                if (isDirectory && Directory.Exists(temp))
                    Directory.Delete(temp, true);
                else if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }
}
