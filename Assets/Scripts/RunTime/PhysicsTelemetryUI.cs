using UnityEngine;
using TMPro;
using UnityEngine.UI;
using SatelliteViewer.Data;

public class PhysicsTelemetryUI : MonoBehaviour
{
    [Header("Reference")]
    public SatellitePhysicsManager physicsManager;

    [Header("Text Fields")]
    public TMP_Text batteryText;
    public TMP_Text tempText;
    public TMP_Text attitudeText;
    public TMP_Text angularRateText;
    public TMP_Text signalText;
    public TMP_Text fuelText;
    public TMP_Text payloadText;
    public TMP_Text powerText;
    public TMP_Text linkText;

    [Header("Optional Sliders")]
    public Slider batterySlider;
    public Slider signalSlider;
    public Slider fuelSlider;
    public Slider attitudeSlider;

    [Header("Settings")]
    public bool autoFindPhysicsManager = true;

    private void Awake()
    {
        if (physicsManager == null && autoFindPhysicsManager)
            physicsManager = FindFirstObjectByType<SatellitePhysicsManager>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        if (physicsManager == null)
            return;

        UpdateTexts();
        UpdateSliders();
    }

    private void UpdateTexts()
    {
        if (batteryText != null)
            batteryText.text = $"Battery: {physicsManager.batteryPercent:0.0}%";

        if (tempText != null)
            tempText.text = $"Temp: {physicsManager.temperatureC:0.0} C";

        if (attitudeText != null)
            attitudeText.text = $"Attitude Error: {physicsManager.attitudeErrorDeg:0.00} deg";

        if (angularRateText != null)
            angularRateText.text = $"Angular Rate: {physicsManager.angularRateDegPerSec:0.000} deg/s";

        if (signalText != null)
            signalText.text = $"Signal: {(physicsManager.signalQuality * 100f):0.0}%";

        if (fuelText != null)
            fuelText.text = $"Fuel: {physicsManager.fuelPercent:0.0}%";

        if (payloadText != null)
            payloadText.text = $"Payload Data: {physicsManager.payloadDataRate:0.00}";

        if (powerText != null)
            powerText.text = $"Power: {(physicsManager.powerAvailable ? "AVAILABLE" : "LOST")}";

        if (linkText != null)
            linkText.text = $"Cmd Link: {(physicsManager.commandLinkAvailable ? "AVAILABLE" : "LOST")}";
    }

    private void UpdateSliders()
    {
        if (batterySlider != null)
            batterySlider.value = physicsManager.batteryPercent / 100f;

        if (signalSlider != null)
            signalSlider.value = physicsManager.signalQuality;

        if (fuelSlider != null)
            fuelSlider.value = physicsManager.fuelPercent / 100f;

        if (attitudeSlider != null)
            attitudeSlider.value = physicsManager.attitudeErrorDeg / 180f;
    }
}