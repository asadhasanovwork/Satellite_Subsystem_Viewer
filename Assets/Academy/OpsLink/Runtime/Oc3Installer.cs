using System;
using System.IO;
using System.Threading.Tasks;

namespace Academy.OpsLink
{
    /// <summary>
    /// Installs (or upgrades) a generated plugin into a local OpenC3 running in Docker:
    /// copies the plugin source into the cmd-tlm-api container, builds the gem there
    /// with its own Ruby, and loads it with openc3cli. No Ruby is needed on the host.
    /// </summary>
    public static class Oc3Installer
    {
        public sealed class Result
        {
            public bool Ok;
            public string Log = "";
            public override string ToString() => (Ok ? "OK" : "FAILED") + "\n" + Log;
        }

        public static Task<Result> InstallAsync(string pluginDir, string gemName, string scope = "DEFAULT") =>
            Task.Run(() => Install(pluginDir, gemName, scope));

        public static Result Install(string pluginDir, string gemName, string scope = "DEFAULT")
        {
            var r = new Result();
            ShellResult ps = ShellProcess.Run("docker", "ps --filter \"name=openc3-cosmos-cmd-tlm-api\" --format \"{{.Names}}\"", null, 20000);
            string container = (ps.Output ?? "").Split('\n')[0].Trim();
            if (!ps.Ok || container.Length == 0)
            {
                r.Log = "OpenC3 cmd-tlm-api container is not running. " + ps;
                return r;
            }
            r.Log += "container: " + container + "\n";

            string remote = "/tmp/opslink_" + gemName;
            Step(r, "docker", $"exec -u root {container} rm -rf {remote}", 30000);
            if (!Step(r, "docker", $"cp \"{Path.GetFullPath(pluginDir)}\" {container}:{remote}", 60000)) return r;
            // docker cp writes files as root; hand them to the openc3 user that builds and loads the gem
            if (!Step(r, "docker", $"exec -u root {container} chown -R openc3:openc3 {remote}", 30000)) return r;

            string script =
                $"cd {remote} && rm -f *.gem && gem build {gemName}.gemspec >/dev/null && " +
                $"GEM=$(ls -t {gemName}-*.gem | head -1) && echo built $GEM && " +
                $"openc3cli load $GEM {scope}";
            r.Ok = Step(r, "docker", $"exec {container} sh -c \"{script}\"", 240000);
            return r;
        }

        static bool Step(Result r, string exe, string args, int timeoutMs)
        {
            ShellResult s = ShellProcess.Run(exe, args, null, timeoutMs);
            r.Log += "$ " + exe + " " + args + "\n" + s + "\n";
            return s.Ok;
        }
    }
}
