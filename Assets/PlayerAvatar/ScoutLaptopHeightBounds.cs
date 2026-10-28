using UnityEngine;
#if UNITY_EDITOR && ENABLE_VR
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using System.Collections.Generic;
#endif

// Opt-in editor test adapter. Filters the simulator's requested Y translation;
// never writes camera, origin, controller or gun transforms, or package assets.
[DefaultExecutionOrder(-30000), DisallowMultipleComponent]
public sealed class ScoutLaptopHeightBounds : MonoBehaviour
#if UNITY_EDITOR && ENABLE_VR
    , IXRInputValueReader<float>
#endif
{
    public ScoutLocalRigBinding binding;
    [Min(.8f)] public float minimumEyeHeight = 1.25f;
    [Min(1.2f)] public float maximumEyeHeight = 1.85f;
#if UNITY_EDITOR && ENABLE_VR
    XRInteractionSimulator simulator;
    XRInputValueReader<float> reader;
    IXRInputValueReader<float> previous;
    bool originalAuto, calibratedByAdapter;
    float originalHeight;
    readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    public bool IsFiltering => reader != null && ReferenceEquals(reader.bypass, this) && SyntheticOnly();
    public float MinimumAllowedHeight => Mathf.Max(minimumEyeHeight,
        binding.CalibratedStandingHeight - (binding.avatar.StandingHeadHeight - binding.EffectiveMinimumCrouchHeight) + .035f);
    public float MaximumAllowedHeight => Mathf.Max(MinimumAllowedHeight + .05f,
        Mathf.Min(maximumEyeHeight, binding.CalibratedStandingHeight + .06f));

    bool SyntheticOnly()
    {
        if (!binding || !binding.HasValidReferences || !simulator || !simulator.isActiveAndEnabled ||
            simulator.cameraTransform != binding.trackedCamera.transform || InputSystem.GetDevice<XRSimulatedHMD>() == null) return false;
        foreach (var device in InputSystem.devices) if (device is XRHMD && !(device is XRSimulatedHMD) && device.enabled) return false;
        SubsystemManager.GetSubsystems(displays);
        foreach (var display in displays) if (display.running) return false;
        return true;
    }
    void Update()
    {
        if (!simulator) simulator = FindAnyObjectByType<XRInteractionSimulator>();
        if (!SyntheticOnly()) { Release(); return; }
        if (reader != null) return;
        var hmd = InputSystem.GetDevice<XRSimulatedHMD>();
        Vector3 world = binding.trackedCamera.transform.parent.TransformPoint(hmd.centerEyePosition.ReadValue());
        float floor = binding.xrOrigin.position.y;
        if (binding.avatar.TryGetComponent<ScoutProceduralLegs>(out var legs) && legs.IsConfigured) floor = legs.PrepareGrounding(world);
        float initialHeight = world.y - floor;
        if (binding.standingEyeHeight <= 0 && (initialHeight < 1.1f || initialHeight > 2.4f)) return;
        reader = simulator.translateYInput; previous = reader.bypass; reader.bypass = this;
        originalAuto = binding.calibrateStandingHeightOnStart; originalHeight = binding.standingEyeHeight;
        // Calibrate once from the initialized synthetic pose and CURRENT floor,
        // before FPS input starts (XRI waits until t>1 s). A raised arena floor
        // must not make the initial standing pose look like a crouch.
        calibratedByAdapter = originalHeight <= 0;
        if (calibratedByAdapter) { binding.standingEyeHeight = initialHeight; binding.calibrateStandingHeightOnStart = false; }
    }
    public float ReadValue()
    {
        float raw = previous != null ? previous.ReadValue() : reader.ReadValue();
        if (!SyntheticOnly() || (!simulator.currentState.manipulatingFPS && !simulator.currentState.manipulatingHMD)) return raw;
        var hmd = InputSystem.GetDevice<XRSimulatedHMD>();
        Vector3 world = binding.trackedCamera.transform.parent.TransformPoint(hmd.centerEyePosition.ReadValue());
        float floor = binding.xrOrigin.position.y;
        if (binding.avatar.TryGetComponent<ScoutProceduralLegs>(out var legs) && legs.IsConfigured) floor = legs.PrepareGrounding(world);
        float scale = simulator.translateYSpeed * simulator.bodyTranslateMultiplier * Time.deltaTime;
        if (Mathf.Abs(scale) < .000001f) return 0;
        float height = world.y - floor;
        // Filtering the requested delta prevents hidden offset accumulation.
        // The opposite key works immediately after holding against a boundary.
        return Mathf.Clamp(raw * scale, MinimumAllowedHeight - height, MaximumAllowedHeight - height) / scale;
    }
    public bool TryReadValue(out float value) { value = ReadValue(); return true; }
    void OnDisable() { Release(); }
    void Release()
    {
        if (reader == null) return;
        if (ReferenceEquals(reader.bypass, this)) reader.bypass = previous;
        if (binding && calibratedByAdapter) { binding.calibrateStandingHeightOnStart = originalAuto; binding.standingEyeHeight = originalHeight; }
        reader = null; previous = null; calibratedByAdapter = false;
    }
#else
    void Awake() { enabled = false; }
#endif
}
