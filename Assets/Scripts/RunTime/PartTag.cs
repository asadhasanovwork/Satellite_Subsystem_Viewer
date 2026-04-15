using UnityEngine;

public class PartTag : MonoBehaviour
{
    public string subsystemId;
    public string partId;
    public string unityName;

    public Renderer[] CachedRenderers { get; private set; }

    private void Awake()
    {
        Cache();
    }

    public void Cache()
    {
        CachedRenderers = GetComponentsInChildren<Renderer>(true);
    }
}