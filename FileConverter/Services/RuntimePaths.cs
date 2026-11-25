using System;
using System.IO;

namespace FileConverter.Services
{
    internal static class RuntimePaths
    {
        internal static string Find(string directory, string requiredFile)
        {
            string[] roots = {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FileConverter"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FileConverter")
            };
            foreach (string root in roots)
            {
                string path = Path.Combine(root, directory);
                if (File.Exists(Path.Combine(path, requiredFile))) return path;
            }
            throw new FileNotFoundException("The offline " + directory + " engine is missing. Install the complete FileConverter package before running this build.");
        }
        internal static string Script(string file)
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TranslationRuntime", file);
            return File.Exists(local) ? local : Path.Combine(Find("TranslationRuntime", "python.exe"), file);
        }
    }
}
