using System;
using System.IO;
using UnityEngine;

namespace Academy.Builder
{
    /// <summary>
    /// Remembers the last complete design made in the Builder, so other screens
    /// (Mission Operations Training) can fly "the satellite you built".
    /// </summary>
    public static class LastDesignStore
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "SatelliteDesigns", "last_built.design.json");

        public static void Remember(DesignSave save)
        {
            if (save == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(save, true));
            }
            catch (Exception e) { Debug.LogWarning("[Builder] Could not remember the design: " + e.Message); }
        }

        public static DesignSave Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var save = JsonUtility.FromJson<DesignSave>(File.ReadAllText(FilePath));
                return save != null && !string.IsNullOrEmpty(save.templateId) ? save : null;
            }
            catch (Exception e) { Debug.LogWarning("[Builder] Could not read the last design: " + e.Message); return null; }
        }
    }
}
