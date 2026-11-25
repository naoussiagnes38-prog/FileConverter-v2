using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using FileConverter.Core.Interfaces;

namespace FileConverter.Core.Services
{
    public sealed class CompressionService : IFileCompressionService
    {
        public void CompressFile(string input, string output, CompressionLevel level)
        { CompressFile(input, output, level, null); }
        public void CompressFile(string input, string output, CompressionLevel level, Action<double> progress, CancellationToken token = default(CancellationToken))
        { CompressFiles(new[] { input }, output, level, progress, token); }

        public void CompressFiles(IEnumerable<string> inputs, string output, CompressionLevel level, Action<double> progress, CancellationToken token = default(CancellationToken))
        {
            var files = inputs.Select(path => new FileInfo(path)).ToArray();
            long total = Math.Max(1, files.Sum(file => file.Length));
            long done = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var archive = ZipFile.Open(output, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    string name = file.Name;
                    for (int copy = 2; !names.Add(name); copy++)
                        name = Path.GetFileNameWithoutExtension(file.Name) + " (" + copy + ")" + file.Extension;
                    var entry = archive.CreateEntry(name, level);
                    if (file.LastWriteTime.Year >= 1980 && file.LastWriteTime.Year <= 2107) entry.LastWriteTime = file.LastWriteTime;
                    using (var source = file.OpenRead())
                    using (var target = entry.Open())
                        Copy(source, target, token, count => { done += count; progress?.Invoke((double)done / total); });
                }
            }
            token.ThrowIfCancellationRequested();
            progress?.Invoke(1);
        }

        public void Decompress(string input, string output, Action<double> progress, CancellationToken token = default(CancellationToken))
        {
            if (!string.Equals(Path.GetExtension(input), ".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a .zip archive to decompress. PowerPoint (.pptx) files are presentation documents; open or convert them instead of extracting their internal package files.");
            string root = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(output);
            using (var archive = ZipFile.OpenRead(input))
            {
                long total = Math.Max(1, archive.Entries.Sum(entry => entry.Length));
                long done = 0;
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    string name = entry.FullName.Replace('\\', '/');
                    string target = Path.GetFullPath(Path.Combine(root, name));
                    if (name.Contains(":") || Path.IsPathRooted(name) || !target.StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidDataException("Unsafe path in ZIP archive: " + entry.FullName);
                    if (name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var source = entry.Open())
                    using (var destination = new FileStream(target, FileMode.CreateNew))
                        Copy(source, destination, token, count => { done += count; progress?.Invoke((double)done / total); });
                }
            }
            token.ThrowIfCancellationRequested();
            progress?.Invoke(1);
        }

        private static void Copy(Stream source, Stream target, CancellationToken token, Action<int> progress)
        {
            var buffer = new byte[81920];
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                target.Write(buffer, 0, count);
                progress(count);
            }
        }
    }
}
