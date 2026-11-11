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

        [MenuItem("Academy/OpsLink/Close OpenC3 Browser")]
        static void CloseBrowser() => OpsBrowserScreen.Close();
    }
}
