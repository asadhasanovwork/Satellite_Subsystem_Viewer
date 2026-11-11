using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Academy.OpsLink
{
    /// <summary>Local OpenC3 connection settings, stored in persistentDataPath/OpsLink/settings.json.</summary>
    [Serializable]
    public sealed class OpsLinkSettings
    {
        public string openc3Url = "http://localhost:2900";
        /// <summary>Folder with OpenC3's compose.yaml. Empty = auto-detect.</summary>
        public string composeDir = "";
        /// <summary>OpenC3 Core password. Set on first start if OpenC3 has none yet.</summary>
        public string password = "academy-test";
        public int linkPort = 7779;
        public string dockerDesktopExe = @"C:\Program Files\Docker\Docker\Docker Desktop.exe";
        public List<InstalledPlugin> installed = new List<InstalledPlugin>();

        [Serializable]
        public sealed class InstalledPlugin { public string target; public string hash; }

        public static string FilePath => Path.Combine(Application.persistentDataPath, "OpsLink", "settings.json");

        public static OpsLinkSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonUtility.FromJson<OpsLinkSettings>(File.ReadAllText(FilePath));
                    if (s != null) return s;
                }
            }
            catch (Exception e) { Debug.LogWarning("[OpsLink] settings.json unreadable, using defaults: " + e.Message); }
            var d = new OpsLinkSettings();
            d.Save();
            return d;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception e) { Debug.LogWarning("[OpsLink] Could not save settings: " + e.Message); }
        }

        public string InstalledHash(string target) => installed.Find(i => i.target == target)?.hash;

        public void SetInstalledHash(string target, string hash)
        {
            var i = installed.Find(x => x.target == target);
            if (i == null) installed.Add(i = new InstalledPlugin { target = target });
            i.hash = hash;
            Save();
        }

        /// <summary>The OpenC3 cosmos-project folder (the one with compose.yaml), or null.</summary>
        public string ResolveComposeDir()
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(composeDir)) candidates.Add(composeDir);
            string root = Path.GetDirectoryName(Application.dataPath) ?? "";
            candidates.Add(Path.Combine(root, "OpenC3 Simulation", "OpenC3 Simulation", "cosmos-project")); // editor / project layout
            candidates.Add(Path.Combine(root, "OpenC3", "cosmos-project"));                                  // next to the built .exe
            candidates.Add(Path.Combine(root, "cosmos-project"));
            foreach (string c in candidates)
                if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "compose.yaml")))
                    return Path.GetFullPath(c);
            return null;
        }
    }
}
