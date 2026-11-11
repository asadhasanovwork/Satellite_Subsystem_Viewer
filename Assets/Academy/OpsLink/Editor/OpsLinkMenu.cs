using UnityEditor;
using UnityEngine;

namespace Academy.OpsLink.EditorTools
{
    static class OpsLinkMenu
    {
        [MenuItem("Academy/OpsLink/Open OpenC3 Browser (Play mode)")]
        static void OpenBrowser()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("OpsLink", "Enter Play mode first, then use this menu again.", "OK");
                return;
            }
            OpsBrowserScreen.Open(OpsBrowserScreen.DefaultUrl, () => Debug.Log("[OpsLink] Back to Academy"));
        }

        [MenuItem("Academy/OpsLink/Connect running Operations to OpenC3")]
        static async void ConnectOperations()
        {
            if (!EditorApplication.isPlaying) { EditorUtility.DisplayDialog("OpsLink", "Open Mission Operations in Play mode first.", "OK"); return; }
            var sim = Object.FindFirstObjectByType<Academy.Operations.OperationsSimulation>();
            if (sim == null || sim.Sim == null) { EditorUtility.DisplayDialog("OpsLink", "No running satellite. Start a lesson or free flight first.", "OK"); return; }
            await OpsLinkSession.StartAsync(sim);
        }

        [MenuItem("Academy/OpsLink/Close OpenC3 Browser")]
        static void CloseBrowser() => OpsBrowserScreen.Close();
    }
}
