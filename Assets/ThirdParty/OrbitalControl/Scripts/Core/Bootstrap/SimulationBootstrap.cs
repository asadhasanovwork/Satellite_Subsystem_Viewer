using UnityEngine;

/// <summary>
/// Attach to a single "Bootstrap" GameObject. Assign all systems in the Inspector.
/// At runtime, builds a SimContext and calls Initialize() on each system in dependency order.
/// </summary>
public sealed class SimulationBootstrap : MonoBehaviour
{
    [SerializeField] private BootstrapReferences references = new();

    private SimContext ctx;

    /// <summary>Academy: context of the running simulation (null when the scene is not loaded).</summary>
    public static SimContext Current { get; private set; }

    private void OnDestroy() { if (Current == ctx) Current = null; }

    /// <summary>
    /// Creates the shared context and initializes all registered systems.
    /// </summary>
    private void Awake()
    {
        if (!BootstrapValidator.TryValidate(references, out string error))
        {
            Debug.LogError(error);
            enabled = false;
            return;
        }

        ctx = SimContextFactory.Create(references);
        BootstrapSequence.Initialize(ctx, references, gameObject);
        Current = ctx;
    }
}
