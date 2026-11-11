using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Academy.OpsLink
{
    public enum LaunchError { None, DockerMissing, DockerNotStarting, ComposeMissing, OpenC3NotStarting, PasswordMismatch, Cancelled }

    /// <summary>
    /// Makes sure a local OpenC3 COSMOS is up: Docker engine running (starts Docker Desktop if
    /// needed), OpenC3 containers up (docker compose up -d), web API answering, password set.
    /// </summary>
    public sealed class OpenC3Launcher
    {
        public LaunchError Error { get; private set; }
        public string ErrorText { get; private set; } = "";

        readonly OpsLinkSettings _s;
        readonly Action<string> _status;
        readonly Oc3Api _api;

        public OpenC3Launcher(OpsLinkSettings settings, Action<string> status)
        {
            _s = settings;
            _status = status ?? (_ => { });
            _api = new Oc3Api(settings.openc3Url);
        }

        public Oc3Api Api => _api;

        public async Task<bool> EnsureReadyAsync(CancellationToken ct)
        {
            try
            {
                // Fast path: OpenC3 already answering
                if (await _api.IsUpAsync() && await _api.TokenExistsAsync() != null)
                    return await EnsurePasswordAsync();

                if (!await EnsureDockerAsync(ct)) return false;
                if (!await EnsureContainersAsync(ct)) return false;
                if (!await WaitForApiAsync(ct)) return false;
                return await EnsurePasswordAsync();
            }
            catch (OperationCanceledException) { return Fail(LaunchError.Cancelled, "Cancelled."); }
        }

        // ------------------------------------------------------------------ docker

        async Task<bool> EnsureDockerAsync(CancellationToken ct)
        {
            _status("Checking Docker");
            ShellResult v = await ShellProcess.RunAsync("docker", "version --format \"{{.Client.Version}}\"", null, 20000);
            if (v.Error != null)
                return Fail(LaunchError.DockerMissing,
                    "Docker is not installed on this computer. Install Docker Desktop (or WSL2 + Docker Engine), then try again.");

            if (await DaemonUpAsync()) return true;

            if (File.Exists(_s.dockerDesktopExe))
            {
                _status("Starting Docker Desktop");
                ShellProcess.StartDetached(_s.dockerDesktopExe);
            }
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromMinutes(3))
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(3000, ct);
                _status($"Waiting for Docker to start ({(int)sw.Elapsed.TotalSeconds} s)");
                if (await DaemonUpAsync()) return true;
            }
            return Fail(LaunchError.DockerNotStarting, "Docker did not start within 3 minutes. Open Docker Desktop, wait until it says 'Engine running', then try again.");
        }

        static async Task<bool> DaemonUpAsync()
        {
            ShellResult r = await ShellProcess.RunAsync("docker", "info --format \"{{.ServerVersion}}\"", null, 15000);
            return r.Ok && r.Output.Trim().Length > 0;
        }

        async Task<bool> EnsureContainersAsync(CancellationToken ct)
        {
            string dir = _s.ResolveComposeDir();
            if (dir == null)
                return Fail(LaunchError.ComposeMissing,
                    "OpenC3 files were not found (compose.yaml). Put the 'cosmos-project' folder next to the program or set composeDir in " + OpsLinkSettings.FilePath);

            _status("Starting OpenC3 (first start downloads several GB and can take a while)");
            // Run detached from this call: pulling images on first start can take many minutes.
            Task<ShellResult> up = ShellProcess.RunAsync("docker", "compose -f compose.yaml up -d", dir, 60 * 60 * 1000);
            var sw = Stopwatch.StartNew();
            while (!up.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(2000, ct);
                _status($"Starting OpenC3 containers ({(int)sw.Elapsed.TotalSeconds} s)");
                if (await _api.IsUpAsync()) break;
            }
            if (up.IsCompleted && !up.Result.Ok && !await _api.IsUpAsync())
                return Fail(LaunchError.OpenC3NotStarting, "docker compose failed:\n" + Tail(up.Result.Output));
            return true;
        }

        async Task<bool> WaitForApiAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromMinutes(10))
            {
                ct.ThrowIfCancellationRequested();
                if (await _api.IsUpAsync() && await _api.TokenExistsAsync() != null) return true;
                _status($"Waiting for OpenC3 to finish starting ({(int)sw.Elapsed.TotalSeconds} s)");
                await Task.Delay(3000, ct);
            }
            return Fail(LaunchError.OpenC3NotStarting, "OpenC3 did not answer at " + _s.openc3Url + " within 10 minutes.");
        }

        async Task<bool> EnsurePasswordAsync()
        {
            bool? exists = await _api.TokenExistsAsync();
            if (exists == false)
            {
                _status("Setting the OpenC3 password");
                await _api.SetPasswordAsync(_s.password);
            }
            string r = await _api.TargetNamesAsync(_s.password);
            if (r.Contains("\"result\"")) return true;
            if (r.Contains("Password is invalid"))
                return Fail(LaunchError.PasswordMismatch,
                    "OpenC3 already has a different password. Put it in 'password' in " + OpsLinkSettings.FilePath);
            return Fail(LaunchError.OpenC3NotStarting, "OpenC3 API error: " + Tail(r));
        }

        bool Fail(LaunchError e, string text)
        {
            Error = e; ErrorText = text;
            return false;
        }

        static string Tail(string s) => s == null ? "" : (s.Length > 600 ? "…" + s.Substring(s.Length - 600) : s);
    }
}
