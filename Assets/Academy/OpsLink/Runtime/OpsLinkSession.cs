using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Academy.Operations;
using UnityEngine;

namespace Academy.OpsLink
{
    /// <summary>
    /// Connects a running <see cref="OperationsSimulation"/> to OpenC3: builds the command/telemetry
    /// dictionary from the simulator, writes the plugin, installs it when it changed and starts the
    /// spacecraft link. Must be called on the main thread while the simulation runs.
    /// </summary>
    public static class OpsLinkSession
    {
        public static Oc3Dictionary Dictionary { get; private set; }
        public static string Status { get; private set; } = "idle";
        public static string LastError { get; private set; } = "";
        public static event Action<string> StatusChanged;

        public static string TargetNameFor(SatelliteSim sim) =>
            "ACADEMY_" + Oc3Dictionary.Sanitize(sim.SatelliteName, "SAT").Replace("ACADEMY_", "");

        public static string PluginRoot => Path.Combine(Application.persistentDataPath, "OpsLink", "plugins");

        /// <summary>URL of the generated "Mission overview" screen's target in Command Sender.</summary>
        public static string CommandSenderUrl(OpsLinkSettings s) =>
            s.openc3Url.TrimEnd('/') + "/tools/cmdsender/" + (Dictionary != null ? Dictionary.TargetName : "");

        public static async Task<bool> StartAsync(OperationsSimulation simulation, OpsLinkSettings settings = null, bool openBrowser = false)
        {
            settings = settings ?? OpsLinkSettings.Load();
            LastError = "";
            if (simulation == null || simulation.Sim == null) return Fail("No running satellite.");

            SetStatus("Building command & telemetry dictionary");
            Dictionary = Oc3Dictionary.Build(simulation.Sim, TargetNameFor(simulation.Sim));

            string dir = Path.Combine(PluginRoot, Dictionary.TargetName.ToLowerInvariant());
            string gem = Oc3PluginWriter.Write(Dictionary, dir, new Oc3PluginWriter.Options { LinkPort = settings.linkPort });
            File.WriteAllText(Path.Combine(dir, "dictionary.json"), JsonUtility.ToJson(Dictionary, true));
            string hash = ContentHash(dir);
            Debug.Log($"[OpsLink] Plugin {gem}: {Dictionary.Telemetry.Count} tlm packets, {Dictionary.Commands.Count} commands, hash {hash.Substring(0, 12)}");

            var link = simulation.GetComponent<SpacecraftLink>();
            if (link == null) link = simulation.gameObject.AddComponent<SpacecraftLink>();
            link.enabled = false;
            link.simulation = simulation;
            link.dictionary = Dictionary;
            link.port = settings.linkPort;
            link.enabled = true;   // (re)starts the listener on the configured port

            var api = new Oc3Api(settings.openc3Url);
            string targets = await api.TargetNamesAsync(settings.password);
            bool targetPresent = targets.Contains("\"" + Dictionary.TargetName + "\"");
            if (targetPresent && settings.InstalledHash(Dictionary.TargetName) == hash)
            {
                SetStatus("Satellite already installed in OpenC3");
            }
            else
            {
                SetStatus(targetPresent ? "Updating the satellite in OpenC3" : "Installing the satellite into OpenC3");
                Oc3Installer.Result r = await Oc3Installer.InstallAsync(dir, gem);
                Debug.Log("[OpsLink] Install " + r);
                if (!r.Ok) return Fail("Plugin install failed:\n" + Tail(r.Log));
                settings.SetInstalledHash(Dictionary.TargetName, hash);
            }

            // Only this satellite's interface may use the simulator port: park every other Academy interface.
            await ActivateOnlyAsync(api, settings.password, Dictionary.TargetName + "_INT");

            SetStatus("Ready");
            if (openBrowser) OpsBrowserScreen.Open(CommandSenderUrl(settings));
            return true;
        }

        static async Task ActivateOnlyAsync(Oc3Api api, string password, string ours)
        {
            try
            {
                string names = await api.RpcAsync("get_interface_names", "[]", password);
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(names, "\"(ACADEMY_[A-Z0-9_]+_INT)\""))
                {
                    string name = m.Groups[1].Value;
                    if (name != ours)
                        await api.RpcAsync("disconnect_interface", "[" + Oc3Api.Json(name) + "]", password);
                }
                await api.RpcAsync("connect_interface", "[" + Oc3Api.Json(ours) + "]", password);
            }
            catch (Exception e) { Debug.LogWarning("[OpsLink] Could not switch interfaces: " + e.Message); }
        }

        /// <summary>Hash of everything that defines the plugin (ignores the gem version stamp).</summary>
        static string ContentHash(string dir)
        {
            var sb = new StringBuilder();
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                                          .Where(f => !f.EndsWith(".gemspec") && !f.EndsWith("dictionary.json"))
                                          .OrderBy(f => f, StringComparer.Ordinal))
                sb.Append(f.Substring(dir.Length)).Append('\n').Append(File.ReadAllText(f));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }

        static bool Fail(string error)
        {
            LastError = error;
            SetStatus("Failed");
            Debug.LogError("[OpsLink] " + error);
            return false;
        }

        static string Tail(string s) => s == null ? "" : (s.Length > 800 ? "…" + s.Substring(s.Length - 800) : s);

        static void SetStatus(string s)
        {
            Status = s;
            Debug.Log("[OpsLink] " + s);
            StatusChanged?.Invoke(s);
        }
    }
}
