using System.Diagnostics;
using System.IO;
using System.Text;
using Stats.Core.Diagnostics;

namespace Stats.App.Helpers;

/// <summary>Rolling daily trace log under %AppData%\Stats\logs\stats-YYYYMMDD.log. <see cref="Install"/> is
/// called first in App.OnStartup, before any sensor/fan initialization, so those failures have a listener to
/// land in. Installation and pruning are both best-effort: a logging failure must never prevent startup.</summary>
internal static class RollingTraceLog
{
    private const int FilesToKeep = 7;

    /// <summary>The logs folder, once <see cref="Install"/> has successfully created it; null if logging
    /// could not be set up (e.g. Open log folder falls back to recomputing the path).</summary>
    public static string? LogDirectory { get; private set; }

    public static void Install()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stats", "logs");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"stats-{DateTime.Now:yyyyMMdd}.log");
            Trace.Listeners.Add(new AtomicAppendTraceListener(path));
            Trace.AutoFlush = true;

            LogDirectory = dir; // only after the listener is live, so LogDirectory never points at an unwritten folder
            Prune(dir);
        }
        catch (Exception)
        {
            // Best-effort: a logging setup failure must not block sensor/fan startup.
        }
    }

    private static void Prune(string dir)
    {
        try
        {
            var names = Directory.EnumerateFiles(dir, "stats-*.log").Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();
            foreach (var name in LogRetention.SelectFilesToPrune(names, FilesToKeep))
            {
                try { File.Delete(Path.Combine(dir, name)); }
                catch (Exception ex) { Trace.WriteLine("[Stats] log prune failed for " + name + ": " + ex.Message); }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[Stats] log prune enumeration failed: " + ex.Message);
        }
    }

    private sealed class AtomicAppendTraceListener : TraceListener
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private readonly FileStream _stream;
        private readonly Mutex _writeMutex = new(false, @"Local\Stats.RollingTraceLog");

        public AtomicAppendTraceListener(string path)
            : base("StatsRollingLog")
        {
            _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        }

        public override void Write(string? message) => Append(message ?? "");

        public override void WriteLine(string? message) => Append((message ?? "") + Environment.NewLine);

        public override void Flush()
        {
            try { WithWriteLock(() => _stream.Flush()); }
            catch (Exception) { /* logging must never disrupt the app */ }
        }

        public override void Close()
        {
            _stream.Dispose();
            _writeMutex.Dispose();
            base.Close();
        }

        private void Append(string text)
        {
            try
            {
                var bytes = Utf8.GetBytes(text);
                WithWriteLock(() =>
                {
                    _stream.Seek(0, SeekOrigin.End);
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                });
            }
            catch (Exception)
            {
                // Best-effort: disk, share, or mutex failures must not escape a Trace call.
            }
        }

        private void WithWriteLock(Action action)
        {
            var acquired = false;
            try
            {
                try
                {
                    acquired = _writeMutex.WaitOne(TimeSpan.FromSeconds(1));
                    if (!acquired) return; // never block app execution on logging
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                action();
            }
            finally
            {
                if (acquired) _writeMutex.ReleaseMutex();
            }
        }
    }
}
