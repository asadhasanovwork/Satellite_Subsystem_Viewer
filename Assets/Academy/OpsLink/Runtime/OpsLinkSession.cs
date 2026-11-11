using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Academy.Builder;
using Academy.Operations;
using UnityEngine;

namespace Academy.OpsLink
{
    /// <summary>One Academy satellite known to OpenC3 and/or this computer.</summary>
    public sealed class SatelliteInfo
    {
        public string Target;        // OpenC3 target name
        public string DisplayName;   // design name as typed in the Builder
        public string TemplateId;
        public string PluginName;    // full OpenC3 plugin id (null = not installed)
        public bool HasDesign;       // can be flown again from here
        public bool IsLatest;        // the last design from the Builder
        public bool Legacy;          // installed by an older OpsLink (shared port); delete recommended
    }

    /// <summary>
    /// Connects a running <see cref="OperationsSimulation"/> to OpenC3 (dictionary → plugin → install when
    /// changed → own TCP port → connect), and lists / deletes Academy satellites in OpenC3.
    /// </summary>
    public static class OpsLinkSession
    {
        public const string GemPrefix = "openc3-cosmos-academy-";
        static readonly string[] Reserved = { "SYSTEM", "UNKNOWN" };

        public static Oc3Dictionary Dictionary { get; private set; }
        public static string Status { get; private set; } = "idle";
        public static string LastError { get; private set; } = "";
        /// <summary>Target of the satellite being flown (or set up) in OpenC3; null when none.</summary>
        public static string CurrentTarget { get; private set; }
        public static event Action<string> StatusChanged;

        public static string PluginRoot => Path.Combine(Application.persistentDataPath, "OpsLink", "plugins");

        public static string CommandSenderUrl(OpsLinkSettings s) =>
            s.openc3Url.TrimEnd('/') + "/tools/cmdsender/" + (Dictionary != null ? Dictionary.TargetName : "");

        /// <summary>OpenC3 target name for a satellite: its own name, unless that clashes with a non-Academy target.</summary>
        public static string TargetNameFor(string satelliteName, IEnumerable<string> existingTargets, OpsLinkSettings s)
        {
            string name = Oc3Dictionary.Sanitize(satelliteName, "SATELLITE");
            bool clash = Reserved.Contains(name) ||
                         (existingTargets != null && existingTargets.Contains(name) && s.Entry(name) == null);
            return clash ? name + "_SAT" : name;
        }

        // ------------------------------------------------------------------ fly

        public static async Task<bool> StartAsync(OperationsSimulation simulation, OpsLinkSettings settings, DesignSave design)
        {
            settings = settings ?? OpsLinkSettings.Load();
            LastError = "";
            if (simulation == null || simulation.Sim == null) return Fail("No running satellite.");
            var api = new Oc3Api(settings.openc3Url);

            List<string> targets = ParseStrings(await api.TargetNamesAsync(settings.password));
            string target = TargetNameFor(simulation.Sim.SatelliteName, targets, settings);

            SetStatus("Building command & telemetry dictionary");
            Dictionary = Oc3Dictionary.Build(simulation.Sim, target);
            var entry = settings.EnsureEntry(target);
            entry.designName = simulation.Sim.SatelliteName;
            entry.templateId = design?.templateId;
            settings.Save();

            string dir = Path.Combine(PluginRoot, target.ToLowerInvariant());
            string gem = Oc3PluginWriter.Write(Dictionary, dir, new Oc3PluginWriter.Options { LinkPort = entry.port });
            File.WriteAllText(Path.Combine(dir, "dictionary.json"), JsonUtility.ToJson(Dictionary, true));
            Oc3Procedures.Write(Dictionary, Path.Combine(dir, "targets", target, "procedures"));
            WriteManeuverProcedure(simulation.Sim, dir, target);
            if (design != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OpsLinkSettings.DesignPath(target)));
                File.WriteAllText(OpsLinkSettings.DesignPath(target), JsonUtility.ToJson(design, true));
            }
            string hash = ContentHash(dir);
            Debug.Log($"[OpsLink] {target}: {Dictionary.Telemetry.Count} tlm packets, {Dictionary.Commands.Count} commands, port {entry.port}");

            // Simulator endpoint on this satellite's own port
            var link = simulation.GetComponent<SpacecraftLink>();
            if (link == null) link = simulation.gameObject.AddComponent<SpacecraftLink>();
            link.enabled = false;
            link.simulation = simulation;
            link.dictionary = Dictionary;
            link.port = entry.port;
            link.enabled = true;
            CurrentTarget = target;

            bool present = targets.Contains(target);
            if (present && entry.hash == hash)
                SetStatus("Satellite already installed in OpenC3");
            else
            {
                SetStatus(present ? "Updating the satellite in OpenC3" : "Installing the satellite into OpenC3");
                Oc3Installer.Result r = await Oc3Installer.InstallAsync(dir, gem);
                Debug.Log("[OpsLink] Install " + r);
                if (!r.Ok) return Fail("Plugin install failed:\n" + Tail(r.Log));
                entry.hash = hash;
                settings.Save();
            }
            if (link == null) return await Abandoned(api, settings, target);

            SetStatus("Connecting OpenC3 to the satellite");
            if (!await ConnectOnlyAsync(api, settings, target + "_INT", link))
            {
                if (link == null) return await Abandoned(api, settings, target);
                return Fail("OpenC3 could not connect to the simulator on port " + entry.port + ".");
            }

            SetStatus("Ready");
            return true;
        }

        /// <summary>Parks every other Academy interface, then connects ours once its microservice is up.</summary>
        static async Task<bool> ConnectOnlyAsync(Oc3Api api, OpsLinkSettings s, string ours, SpacecraftLink link)
        {
            string pw = s.password;
            var academyTargets = new HashSet<string>(s.installed.Select(i => i.target));
            foreach (string name in ParseStrings(await api.RpcAsync("get_interface_names", "[]", pw)))
            {
                if (name == ours || !name.EndsWith("_INT")) continue;
                string t = name.Substring(0, name.Length - 4);
                if (academyTargets.Contains(t) || name.StartsWith("ACADEMY_"))
                    await api.RpcAsync("disconnect_interface", "[" + Oc3Api.Json(name) + "]", pw);
            }

            var deadline = DateTime.UtcNow.AddSeconds(60);
            DateTime nextConnect = DateTime.MinValue;
            while (DateTime.UtcNow < deadline)
            {
                if (link == null) return false; // the learner left Mission Operations
                string info = await api.RpcAsync("get_interface", "[" + Oc3Api.Json(ours) + "]", pw);
                if (info.Contains("\"state\":\"CONNECTED\"")) return true;
                if (info.Contains("\"result\"") && DateTime.UtcNow >= nextConnect)
                {
                    await api.RpcAsync("connect_interface", "[" + Oc3Api.Json(ours) + "]", pw);
                    nextConnect = DateTime.UtcNow.AddSeconds(6);
                }
                await Task.Delay(1000);
            }
            return false;
        }

        // ------------------------------------------------------------------ list / delete

        public static async Task<List<SatelliteInfo>> ListAsync(OpsLinkSettings s)
        {
            var api = new Oc3Api(s.openc3Url);
            var plugins = ParseStrings(await api.GetAsync("/openc3-api/plugins?scope=DEFAULT", s.password))
                          .Where(p => p.StartsWith(GemPrefix)).ToList();
            var list = new List<SatelliteInfo>();
            // Entries from before per-satellite ports (shared port 7779): their plugins are listed as "old format"
            if (s.installed.RemoveAll(e => e.port <= 0) > 0) s.Save();

            foreach (var e in s.installed)
            {
                string gemBase = GemPrefix + e.target.ToLowerInvariant().Replace('_', '-') + "-";
                list.Add(new SatelliteInfo
                {
                    Target = e.target,
                    DisplayName = string.IsNullOrEmpty(e.designName) ? e.target : e.designName,
                    TemplateId = e.templateId,
                    PluginName = plugins.FirstOrDefault(p => p.StartsWith(gemBase) && IsVersionAfter(p, gemBase)),
                    HasDesign = File.Exists(OpsLinkSettings.DesignPath(e.target)),
                });
            }
            // Plugins installed by an older OpsLink (or removed from settings): offer Delete
            foreach (string p in plugins)
            {
                if (list.Any(x => x.PluginName == p)) continue;
                string slug = Regex.Replace(p.Substring(GemPrefix.Length), @"-\d+\.\d+\.\d+\.gem__\d+$", "");
                list.Add(new SatelliteInfo
                {
                    Target = slug.ToUpperInvariant().Replace('-', '_'),
                    DisplayName = slug.Replace('-', ' '),
                    PluginName = p,
                    Legacy = true,
                });
            }
            return list;
        }

        // "openc3-cosmos-academy-test-1.0.x" must not match the satellite "test-cubesat"
        static bool IsVersionAfter(string plugin, string gemBase) =>
            plugin.Length > gemBase.Length && char.IsDigit(plugin[gemBase.Length]);

        public static async Task<bool> DeleteAsync(OpsLinkSettings s, SatelliteInfo sat)
        {
            var api = new Oc3Api(s.openc3Url);
            bool ok = true;
            if (!string.IsNullOrEmpty(sat.PluginName))
            {
                await api.RpcAsync("disconnect_interface", "[" + Oc3Api.Json(sat.Target + "_INT") + "]", s.password);
                ok = await api.DeleteAsync("/openc3-api/plugins/" + Uri.EscapeDataString(sat.PluginName) + "?scope=DEFAULT", s.password);
            }
            if (ok)
            {
                s.Remove(sat.Target);
                try
                {
                    string dir = Path.Combine(PluginRoot, sat.Target.ToLowerInvariant());
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch { }
            }
            return ok;
        }

        public static DesignSave LoadDesign(string target)
        {
            try
            {
                string p = OpsLinkSettings.DesignPath(target);
                return File.Exists(p) ? JsonUtility.FromJson<DesignSave>(File.ReadAllText(p)) : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>All JSON string literals of a "result" array, e.g. ["A","B"].</summary>
        static List<string> ParseStrings(string json)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(json)) return list;
            int start = json.IndexOf("\"result\"", StringComparison.Ordinal);
            string body = start >= 0 ? json.Substring(start + 8) : json;
            foreach (Match m in Regex.Matches(body, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                list.Add(m.Groups[1].Value);
            return list;
        }

        /// <summary>
        /// Manoeuvre plan from the Orbit Simulation → Script Runner procedure targets/&lt;T&gt;/procedures/maneuver_plan.rb
        /// (removed again when the satellite has no plan).
        /// </summary>
        static void WriteManeuverProcedure(SatelliteSim sim, string dir, string target)
        {
            string path = Path.Combine(dir, "targets", target, "procedures", Academy.OrbitSim.ManeuverPlanProcedure.FileName);
            Academy.OrbitSim.ManeuverPlan plan = Academy.OrbitSim.ManeuverPlanStore.Load(sim.SatelliteName);
            ThrusterSet engine = null;
            foreach (ThrusterSet t in sim.ThrusterSets)
                if (t.Tank != null && (engine == null || t.ThrustN * Math.Max(1, t.Thrusters.Count) > engine.ThrustN * Math.Max(1, engine.Thrusters.Count))) engine = t;
            try
            {
                if (plan == null || engine == null)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, Academy.OrbitSim.ManeuverPlanProcedure.Ruby(plan, target, engine.Id));
                Debug.Log($"[OpsLink] {target}: manoeuvre plan with {plan.burns.Count} burn(s) → Script Runner {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpsLink] Could not write the manoeuvre procedure: " + e.Message);
            }
        }

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

        /// <summary>The learner left Mission Operations while OpenC3 was being set up: park the interface quietly.</summary>
        static async Task<bool> Abandoned(Oc3Api api, OpsLinkSettings s, string target)
        {
            LastError = "";
            SetStatus("Cancelled");
            Debug.Log("[OpsLink] Left Mission Operations before OpenC3 connected; " + target + " parked.");
            try { await api.RpcAsync("disconnect_interface", "[" + Oc3Api.Json(target + "_INT") + "]", s.password); } catch { }
            if (CurrentTarget == target) CurrentTarget = null;
            return false;
        }

        /// <summary>
        /// Leaving Mission Operations: disconnect the flown satellite's interface so OpenC3 stops retrying a simulator
        /// that is no longer running (fire and forget).
        /// </summary>
        public static async void Park(OpsLinkSettings s)
        {
            string target = CurrentTarget;
            CurrentTarget = null;
            if (string.IsNullOrEmpty(target)) return;
            s = s ?? OpsLinkSettings.Load();
            try { await new Oc3Api(s.openc3Url).RpcAsync("disconnect_interface", "[" + Oc3Api.Json(target + "_INT") + "]", s.password); }
            catch (Exception e) { Debug.LogWarning("[OpsLink] Could not park " + target + ": " + e.Message); }
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
