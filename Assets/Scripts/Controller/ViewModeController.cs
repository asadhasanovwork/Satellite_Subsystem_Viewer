using UnityEngine;
using SatelliteViewer.Data;

public class ViewModeController : MonoBehaviour
{
    public VisibilityManager visibility;
    public ExplodeController explode;
    public CameraOrbitMMB orbitCamera;

    public ViewerMode CurrentMode { get; private set; } = ViewerMode.Normal;
    public string CurrentSubsystemId { get; private set; }
    public GameObject CurrentFocusedPart { get; private set; }

    private void Awake()
    {
        if (visibility == null) visibility = FindFirstObjectByType<VisibilityManager>(FindObjectsInactive.Include);
        if (explode == null) explode = FindFirstObjectByType<ExplodeController>(FindObjectsInactive.Include);
        if (orbitCamera == null) orbitCamera = FindFirstObjectByType<CameraOrbitMMB>(FindObjectsInactive.Include);
    }

    public void EnterNormalMode()
    {
        CurrentMode = ViewerMode.Normal;
        CurrentSubsystemId = null;
        CurrentFocusedPart = null;

        explode?.Assemble();
        visibility?.ShowAll();

        if (orbitCamera != null && visibility != null && visibility.satelliteRoot != null)
            orbitCamera.SetTarget(visibility.satelliteRoot);
    }

    public void IsolateSubsystem(string subsystemId)
    {
        if (string.IsNullOrWhiteSpace(subsystemId))
        {
            EnterNormalMode();
            return;
        }

        CurrentMode = ViewerMode.SubsystemIsolation;
        CurrentSubsystemId = subsystemId;
        CurrentFocusedPart = null;

        explode?.Assemble();
        visibility?.ShowOnlySubsystem(subsystemId);

        if (orbitCamera != null && visibility != null && visibility.satelliteRoot != null)
            orbitCamera.SetTarget(visibility.satelliteRoot);
    }

    public void FocusPart(GameObject partRoot)
    {
        if (!partRoot)
        {
            EnterNormalMode();
            return;
        }

        CurrentMode = ViewerMode.PartFocus;
        CurrentFocusedPart = partRoot;

        explode?.Assemble();
        visibility?.ShowOnly(partRoot);

        if (orbitCamera != null)
        {
            orbitCamera.SetTarget(partRoot.transform);

            var renderers = partRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);

                orbitCamera.FrameBounds(b);
            }
        }
    }

    public void SetExploded(float amount01)
    {
        CurrentMode = ViewerMode.Exploded;
        explode?.SetExplode(amount01);
    }

    public void Assemble()
    {
        explode?.Assemble();

        if (CurrentMode == ViewerMode.PartFocus && CurrentFocusedPart != null)
            visibility?.ShowOnly(CurrentFocusedPart);
        else if (CurrentMode == ViewerMode.SubsystemIsolation && !string.IsNullOrWhiteSpace(CurrentSubsystemId))
            visibility?.ShowOnlySubsystem(CurrentSubsystemId);
        else
            visibility?.ShowAll();
    }
}