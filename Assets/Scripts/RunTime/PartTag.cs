using System.Collections.Generic;
using UnityEngine;

public class PartTag : MonoBehaviour
{
    public string subsystemId;
    public string partId;
    public string unityName;

    public Renderer[] CachedRenderers { get; private set; }

    private static readonly List<Renderer> s_Buffer = new List<Renderer>();

    private void Awake()
    {
        Cache();
    }

    public void Cache()
    {
        // Only keep renderers that belong to this part, not to a nested part that has its own PartTag,
        // so isolating/colouring a parent assembly does not also drive its tagged children.
        var all = GetComponentsInChildren<Renderer>(true);
        s_Buffer.Clear();
        foreach (var r in all)
        {
            if (r && r.GetComponentInParent<PartTag>(true) == this)
                s_Buffer.Add(r);
        }

        CachedRenderers = s_Buffer.ToArray();
        s_Buffer.Clear();
    }
}