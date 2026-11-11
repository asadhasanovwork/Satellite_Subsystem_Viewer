using System;
using System.IO;
using System.Threading.Tasks;
using Academy.Operations;
using UnityEngine;

namespace Academy.OpsLink
{
    /// <summary>
    /// Connects a running <see cref="OperationsSimulation"/> to OpenC3: builds the command/telemetry
    /// dictionary from the simulator, writes and installs the plugin, starts the spacecraft link
    /// and shows OpenC3 full screen.
    /// </summary>
    public static class OpsLinkSession
    {
        public const string OpenC3Url = "http://localhost:2900";

        public static Oc3Dictionary Dictionary { get; private set; }
        public static string Status { get; private set; } = "idle";
        public static event Action<string> StatusChanged;

        public static string TargetNameFor(SatelliteSim sim) =>
            "ACADEMY_" + Oc3Dictionary.Sanitize(sim.SatelliteName, "SAT").Replace("ACADEMY_", "");

        public static string PluginRoot => Path.Combine(Application.persistentDataPath, "OpsLink", "plugins");

        /// <summary>Start everything. Must be called on the main thread while the simulation runs.</summary>
        public static async Task<bool> StartAsync(OperationsSimulation simulation, bool openBrowser = true)
        {
            if (simulation == null || simulation.Sim == null) { SetStatus("No running satellite"); return false; }

            SetStatus("Building command & telemetry dictionary");
            Dictionary = Oc3Dictionary.Build(simulation.Sim, TargetNameFor(simulation.Sim));

            string dir = Path.Combine(PluginRoot, Dictionary.TargetName.ToLowerInvariant());
            string gem = Oc3PluginWriter.Write(Dictionary, dir);
            File.WriteAllText(Path.Combine(dir, "dictionary.json"), JsonUtility.ToJson(Dictionary, true));
            Debug.Log($"[OpsLink] Plugin written: {dir} ({Dictionary.Telemetry.Count} tlm packets, {Dictionary.Commands.Count} commands)");

            var link = simulation.GetComponent<SpacecraftLink>() ?? simulation.gameObject.AddComponent<SpacecraftLink>();
            link.simulation = simulation;
            link.dictionary = Dictionary;

            SetStatus("Installing plugin into OpenC3");
            Oc3Installer.Result r = await Oc3Installer.InstallAsync(dir, gem);
            Debug.Log("[OpsLink] Install " + r);
            if (!r.Ok) { SetStatus("Plugin install failed (see console)"); return false; }

            SetStatus("Ready");
            if (openBrowser)
                OpsBrowserScreen.Open(OpenC3Url + "/tools/cmdsender/" + Dictionary.TargetName);
            return true;
        }

        static void SetStatus(string s)
        {
            Status = s;
            Debug.Log("[OpsLink] " + s);
            StatusChanged?.Invoke(s);
        }
    }
}
