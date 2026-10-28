// =============================================================================
// SCRIPT: CinematicOrbit.cs
// =============================================================================
// WHAT IT DOES: 
// Takes over the Main Camera when F12 is pressed to create a looping 3D screensaver.
// FIXES APPLIED:
// - Height bob is strictly clamped so it never clips underground.
// - Fully documented properties for Open Day integration.
// =============================================================================

using UnityEngine;
using UnityEngine.InputSystem;

public class CinematicOrbit : MonoBehaviour
{
    [Header("Target References")]
    [Tooltip("Drag the OpenDay_G_class rocket here")]
    public Transform rocket;
    [Tooltip("Drag the rocket here to link the physics script")]
    public PhyAndControls phy;
    [Tooltip("Drag the Main Camera here to link its normal tracking script")]
    public CameraTrack normalCameraTracker;
    [Tooltip("Drag the rocket here to link the HUD script so we can hide it")]
    public HUD telemetryHUD;

    [Header("Cinematic Settings")]
    public float orbitSpeed = 12f; 
    public float orbitDistance = 3.5f; 
    public float baseHeight = 1.0f;
    public float heightBobAmplitude = 0.6f; 
    public float bobSpeed = 0.5f;
    [Tooltip("Absolute lowest the camera can go (prevents going underground)")]
    public float minHeightLimit = 0.2f;

    private bool isScreensaverActive = false;
    private float orbitAngle = 0f;
    private Quaternion savedNormalRotation;

    void Start() { savedNormalRotation = transform.rotation; }

    void Update()
    {
        if (Keyboard.current == null) return;
        if (Keyboard.current.f12Key.wasPressedThisFrame)
        {
            isScreensaverActive = !isScreensaverActive;
            if (normalCameraTracker != null) normalCameraTracker.enabled = !isScreensaverActive;
            if (telemetryHUD != null) telemetryHUD.enabled = !isScreensaverActive;

            if (isScreensaverActive) {
                if (phy != null) phy.ResetRocket();
                orbitAngle = 45f; 
            } else {
                transform.rotation = savedNormalRotation;
            }
        }
    }

    void LateUpdate()
    {
        if (isScreensaverActive && rocket != null)
        {
            orbitAngle += orbitSpeed * Time.deltaTime;
            float radians = orbitAngle * Mathf.Deg2Rad;

            // Calculates the bobbing height and mathematically prevents underground clipping
            float rawHeight = baseHeight + Mathf.Sin(Time.time * bobSpeed) * heightBobAmplitude;
            float safeHeight = Mathf.Max(minHeightLimit, rawHeight);

            Vector3 orbitOffset = new Vector3(Mathf.Sin(radians) * orbitDistance, safeHeight, Mathf.Cos(radians) * orbitDistance);
            transform.position = rocket.position + orbitOffset;
            transform.LookAt(rocket.position + Vector3.up * 0.4f);
        }
    }
}