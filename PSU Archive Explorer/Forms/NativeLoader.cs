using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace psu_archive_explorer
{
    /// <summary>
    /// Extracts native DLLs and data files from embedded resources to a temp
    /// folder on first run, then explicitly loads them via LoadLibrary so
    /// P/Invoke can find them regardless of the DLL search path.
    ///
    /// Call NativeLoader.EnsureLoaded() once at startup in Program.cs before
    /// Application.Run — it must be the very first thing that runs.
    ///
    /// DLLs are embedded via Build Action: Embedded Resource in the .csproj.
    /// They are accessed via Assembly.GetManifestResourceStream, NOT via
    /// Properties.Resources (which is only for resources added through the
    /// Resources designer).
    /// </summary>
    internal static class NativeLoader
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// Path to the directory where all native files are extracted.
        /// Use this to locate psu_file_index.gz at runtime.
        /// </summary>
        public static readonly string ExtractDir = Path.Combine(
            Path.GetTempPath(), "PSUArchiveExplorer", "native");

        private static bool _loaded = false;
        private static readonly object _lock = new object();

        // Each entry: (embeddedResourceName, outputFileName, preload)
        // The embedded resource name is the namespace + filename as it appears
        // in the assembly manifest. For a file in the project root it is
        // typically "psu_archive_explorer.filename.ext".
        // Check the exact names by calling ListManifestResources() below.
        private static readonly (string ResourceName, string FileName, bool Preload)[] Files =
        {
            ("psu_archive_explorer.libwinpthread-1.dll", "libwinpthread-1.dll", true),
            ("psu_archive_explorer.avutil-60.dll",       "avutil-60.dll",       true),
            ("psu_archive_explorer.swresample-6.dll",    "swresample-6.dll",    true),
            ("psu_archive_explorer.swscale-9.dll",       "swscale-9.dll",       true),
            ("psu_archive_explorer.avcodec-62.dll",      "avcodec-62.dll",      true),
            ("psu_archive_explorer.avformat-62.dll",     "avformat-62.dll",     true),
            ("psu_archive_explorer.avfilter-11.dll",     "avfilter-11.dll",     true),
            ("psu_archive_explorer.ffmpeg_helpers.dll",  "ffmpeg_helpers.dll",  true),
            ("psu_archive_explorer.pl_mpeg.dll",         "pl_mpeg.dll",         true),
            ("psu_archive_explorer.psu_file_index.gz",   "psu_file_index.gz",   false),
        };

        /// <summary>
        /// Ensures all native files are extracted and preloaded.
        /// Safe to call multiple times — only does work on the first call.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                try
                {
                    Directory.CreateDirectory(ExtractDir);
                    SetDllDirectory(ExtractDir);
                    ExtractFiles();

                    // Explicitly load each DLL by full path so P/Invoke finds
                    // them in the already-loaded module list — bypasses all
                    // search path issues entirely.
                    foreach (var (_, fileName, preload) in Files)
                    {
                        if (!preload) continue;
                        string fullPath = Path.Combine(ExtractDir, fileName);
                        if (!File.Exists(fullPath)) continue;

                        IntPtr handle = LoadLibrary(fullPath);
                        if (handle == IntPtr.Zero)
                        {
                            int err = Marshal.GetLastWin32Error();
                            throw new InvalidOperationException(
                                $"Failed to load {fileName} " +
                                $"(Win32 error {err}).\n\nPath: {fullPath}");
                        }
                    }

                    _loaded = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Failed to initialise native libraries:\n\n" +
                        ex.Message + "\n\n" +
                        "Try running the application as administrator, or " +
                        "check that your temp folder is writable.", ex);
                }
            }
        }

        /// <summary>
        /// Full path to the extracted psu_file_index.gz.
        /// Only valid after EnsureLoaded() has been called.
        /// </summary>
        public static string PsuFileIndexPath =>
            Path.Combine(ExtractDir, "psu_file_index.gz");

        /// <summary>
        /// Call this once temporarily to print all embedded resource names to
        /// the console — use it to verify the exact resource name strings if
        /// DLLs are not being found. Remove after confirming names are correct.
        /// </summary>
        public static string ListManifestResources()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string name in Assembly.GetExecutingAssembly()
                                            .GetManifestResourceNames())
                sb.AppendLine(name);
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private static void ExtractFiles()
        {
            Assembly asm = Assembly.GetExecutingAssembly();

            foreach (var (resourceName, fileName, _) in Files)
            {
                string destPath = Path.Combine(ExtractDir, fileName);

                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        // Resource not found — skip silently.
                        // Call ListManifestResources() to debug missing names.
                        continue;
                    }

                    // Only write if file is missing or size differs.
                    if (File.Exists(destPath) &&
                        new FileInfo(destPath).Length == stream.Length)
                        continue;

                    using (var fs = File.Create(destPath))
                        stream.CopyTo(fs);
                }
            }
        }
    }
}