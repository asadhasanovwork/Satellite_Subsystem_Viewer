using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Academy.OpsLink
{
    /// <summary>Result of running an external program.</summary>
    public readonly struct ShellResult
    {
        public readonly int ExitCode;
        public readonly string Output;
        public readonly bool TimedOut;
        public readonly string Error;   // set when the program could not be started

        public ShellResult(int exitCode, string output, bool timedOut, string error)
        {
            ExitCode = exitCode; Output = output; TimedOut = timedOut; Error = error;
        }

        public bool Ok => Error == null && !TimedOut && ExitCode == 0;
        public override string ToString() =>
            Error != null ? $"[not started] {Error}" :
            TimedOut ? $"[timeout] {Output}" : $"[exit {ExitCode}] {Output}";
    }

    /// <summary>
    /// Runs external programs (docker, msedge, ...) without a console window and
    /// captures their combined stdout/stderr. Works in the Editor and in builds.
    /// </summary>
    public static class ShellProcess
    {
        public static ShellResult Run(string exe, string args, string workingDir = null, int timeoutMs = 60000)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;

            var sb = new StringBuilder();
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception e) { return new ShellResult(-1, "", false, e.Message); }

            using (p)
            {
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { /* already gone */ }
                    lock (sb) return new ShellResult(-1, sb.ToString(), true, null);
                }
                p.WaitForExit(); // flush async readers
                lock (sb) return new ShellResult(p.ExitCode, sb.ToString().TrimEnd(), false, null);
            }
        }

        /// <summary>Starts a GUI program (e.g. Docker Desktop) and returns immediately.</summary>
        public static string StartDetached(string exe, string args = "")
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        public static Task<ShellResult> RunAsync(string exe, string args, string workingDir = null, int timeoutMs = 60000) =>
            Task.Run(() => Run(exe, args, workingDir, timeoutMs));
    }
}
