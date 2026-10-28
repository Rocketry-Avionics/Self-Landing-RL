// =============================================================================
// SCRIPT 3: WindAndInput.cs
// =============================================================================
// WHAT THIS CODE DOES:
// This script handles all user keyboard inputs ([Space] Launch, [R] Reset,
// [S] Toggle PID, [D] Manual Wind Gust) AND generates natural atmospheric
// wind turbulence during flight.
//
// HOW NATURAL WIND GUSTS WORK:
// Instead of applying an instant, robotic 1-frame snap to the rocket, each wind
// gust swells smoothly over 0.35 seconds using a sine-wave envelope (Mathf.Sin).
// This pushes the rocket gradually like a real aerodynamic air current!
// =============================================================================

using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PhyAndControls))]
[RequireComponent(typeof(PIDController))]
public class WindAndInput : MonoBehaviour
{
    [Header("Wind Gust Strength Settings (D Key & Auto)")]
    [Tooltip("Rotational kick strength applied over the duration of a wind gust.")]
    public float disturbanceKickSpeed = 2.05f;
    [Tooltip("Lateral sideways drift force applied during a wind gust.")]
    public float disturbanceSidePush = 0.85f;
    [Tooltip("How long (in seconds) each wind gust smoothly swells and fades.")]
    public float gustDuration = 0.35f;

    [Header("Automatic Ambient Wind Gusts")]
    [Tooltip("Automatically triggers natural wind gusts periodically during flight.")]
    public bool enableAutoRandomGusts = true;
    public float minGustInterval = 2.8f;
    public float maxGustInterval = 4.5f;

    private PhyAndControls phy;
    private PIDController pid;

    // State tracking for cumulative vs. alternating gusts
    private float lastDisturbDir = 1f;
    private float lastDisturbTime = -10f;
    private float nextAutoGustTime = 0f;

    // Active smooth gust envelope variables
    private bool isGustActive = false;
    private float currentGustTimer = 0f;
    private float activeGustTorqueRate = 0f;
    private float activeGustForceRate = 0f;

    void Start()
    {
        phy = GetComponent<PhyAndControls>();
        pid = GetComponent<PIDController>();
        ScheduleNextAutoGust();
    }

    void Update()
    {
        if (!Application.isPlaying || Keyboard.current == null) return;

        // SPACE or W: Launch Rocket
        if ((Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) && !phy.IsLaunched)
        {
            phy.LaunchRocket();
            ScheduleNextAutoGust();
        }

        // R KEY: Reset Rocket to Launchpad
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            isGustActive = false;
            phy.ResetRocket();
            ScheduleNextAutoGust();
        }

        // S KEY: Toggle PID Stabilization Loop
        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            pid.TogglePID();
        }

        // D KEY: Manual On-Command Wind Disturbance
        if (Keyboard.current.dKey.wasPressedThisFrame && phy.IsLaunched && !phy.IsCrashed)
        {
            TriggerWindGust(isManualKey: true);
            ScheduleNextAutoGust();
        }

        // Automatic periodic random wind gusts while flying
        if (enableAutoRandomGusts && phy.IsLaunched && !phy.IsStalled && !phy.IsCrashed && phy.currentBurnTime > 0.8f)
        {
            if (Time.time >= nextAutoGustTime)
            {
                TriggerWindGust(isManualKey: false);
                ScheduleNextAutoGust();
            }
        }
    }

    // Applies the active wind gust smoothly across physics frames using a sine bell curve
    void FixedUpdate()
    {
        if (!Application.isPlaying || !isGustActive || !phy.IsLaunched || phy.IsCrashed) return;

        currentGustTimer += Time.fixedDeltaTime;
        if (currentGustTimer >= gustDuration)
        {
            isGustActive = false;
            return;
        }

        // Sine wave bell curve (0 -> 1 -> 0) so the wind swells and fades naturally
        float progress = currentGustTimer / gustDuration;
        float smoothEnvelope = Mathf.Sin(progress * Mathf.PI) * (Mathf.PI / 2f);

        float stepFactor = (Time.fixedDeltaTime / gustDuration) * smoothEnvelope;
        phy.Rb.angularVelocity += new Vector3(0f, 0f, activeGustTorqueRate * stepFactor);
        phy.Rb.AddForce(new Vector3(activeGustForceRate * stepFactor, 0f, 0f), ForceMode.VelocityChange);
    }

    void ScheduleNextAutoGust()
    {
        nextAutoGustTime = Time.time + Random.Range(minGustInterval, maxGustInterval);
    }

    /// <summary>
    /// Initiates a smooth wind gust. Rapid manual presses (< 2.2s apart) stack in the
    /// same direction so 3 rapid presses of [D] can overwhelm the PID and demonstrate stall.
    /// </summary>
    public void TriggerWindGust(bool isManualKey)
    {
        float timeSinceLastGust = Time.time - lastDisturbTime;

        if (isManualKey)
        {
            // Single D press after waiting picks a random side; rapid D presses stack on the same side!
            if (timeSinceLastGust > 2.2f)
            {
                lastDisturbDir = Random.value > 0.5f ? 1f : -1f;
            }
        }
        else
        {
            // Ambient auto-gusts pick a random direction
            lastDisturbDir = Random.value > 0.5f ? 1f : -1f;
        }

        lastDisturbTime = Time.time;
        float intensity = isManualKey ? Random.Range(0.95f, 1.15f) : Random.Range(0.65f, 0.90f);

        // If a gust is already mid-blow, add to it so rapid D presses compound smoothly!
        float remainingFraction = isGustActive ? (1f - (currentGustTimer / gustDuration)) : 0f;
        activeGustTorqueRate = (activeGustTorqueRate * remainingFraction) + (lastDisturbDir * disturbanceKickSpeed * intensity);
        activeGustForceRate = (activeGustForceRate * remainingFraction) + (-lastDisturbDir * disturbanceSidePush * intensity);

        currentGustTimer = 0f;
        isGustActive = true;
    }
}