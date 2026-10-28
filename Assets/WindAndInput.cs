// =============================================================================
// SCRIPT 3: WindAndInput.cs
// =============================================================================
// WHAT IT DOES: 
// Handles Keyboard Inputs (Launch, Reset, PID toggles) and generates WIND.
// Features Perlin Noise Turbulence: Adds high-frequency flutter to the wind 
// forces so it physically judders the rocket instead of pushing robotically smooth.
// Broadcasts wind telemetry to the HUD for animated warnings.
// =============================================================================

using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PhyAndControls))]
[RequireComponent(typeof(PIDController))]
public class WindAndInput : MonoBehaviour
{
    [Header("Manual Arrow-Key Wind Strength")]
    public float windTorqueRate = 12.0f; 
    public float windPushRate = 5.0f;    

    [Header("Automatic Ambient Wind Gusts")]
    public bool enableAutoRandomGusts = true;
    public float minGustInterval = 3.5f; 
    public float maxGustInterval = 6.0f;
    public float autoGustDuration = 0.4f;
    public float autoGustStrength = 1.8f;

    [Header("Wind Realism (Perlin Turbulence)")]
    [Tooltip("How fast the wind shutters and changes strength.")]
    public float turbulenceSpeed = 12.0f; 
    [Tooltip("How violently the wind fluctuates (0.4 = +/- 40% jitter).")]
    public float turbulenceVariance = 0.4f; 

    // Telemetry for the HUD Wind Animation
    public bool IsWindBlowing { get; private set; } = false;
    public float CurrentWindDir { get; private set; } = 0f;

    private PhyAndControls phy;
    private PIDController pid;
    
    // Auto Gust logic
    private float nextAutoGustTime = 0f;
    private bool isAutoGustActive = false;
    private float currentAutoGustTimer = 0f;
    private float activeGustDir = 1f;
    
    // Noise offset prevents looping patterns
    private float noiseOffset;

    void Start()
    {
        phy = GetComponent<PhyAndControls>();
        pid = GetComponent<PIDController>();
        noiseOffset = Random.Range(0f, 1000f);
        ScheduleNextAutoGust();
    }

    void Update()
    {
        if (!Application.isPlaying || Keyboard.current == null) return;

        // Launch & Reset
        if ((Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) && !phy.IsLaunched) {
            phy.LaunchRocket(); ScheduleNextAutoGust();
        }
        if (Keyboard.current.rKey.wasPressedThisFrame) {
            phy.ResetRocket(); isAutoGustActive = false; ScheduleNextAutoGust();
        }

        // Avionics Toggles
        if (Keyboard.current.sKey.wasPressedThisFrame) pid.ToggleMasterPID();
        if (Keyboard.current.digit1Key.wasPressedThisFrame) pid.useP = !pid.useP;
        if (Keyboard.current.digit2Key.wasPressedThisFrame) pid.useI = !pid.useI;
        if (Keyboard.current.digit3Key.wasPressedThisFrame) pid.useD = !pid.useD;

        // Auto-Gust Trigger
        if (enableAutoRandomGusts && phy.IsLaunched && !phy.IsStalled && !phy.IsCrashed && phy.currentBurnTime > 0.8f) {
            if (Time.time >= nextAutoGustTime && !isAutoGustActive) { TriggerAutoGust(); ScheduleNextAutoGust(); }
        }
    }

    void ScheduleNextAutoGust() { nextAutoGustTime = Time.time + Random.Range(minGustInterval, maxGustInterval); }

    void TriggerAutoGust() { activeGustDir = Random.value > 0.5f ? 1f : -1f; currentAutoGustTimer = 0f; isAutoGustActive = true; }

    void FixedUpdate()
    {
        IsWindBlowing = false;
        CurrentWindDir = 0f;

        if (!Application.isPlaying || !phy.IsLaunched || phy.IsCrashed) return;

        // Generate Perlin Noise Jitter multiplier (fluctuates between 0.6x and 1.4x)
        float rawNoise = Mathf.PerlinNoise((Time.time + noiseOffset) * turbulenceSpeed, 0f);
        float jitterMultiplier = 1f + ((rawNoise - 0.5f) * 2f * turbulenceVariance);

        // 1. MANUAL ARROW KEY WIND (With Turbulence)
        float windInput = 0f;
        if (Keyboard.current.leftArrowKey.isPressed) windInput = -1f;
        if (Keyboard.current.rightArrowKey.isPressed) windInput = 1f;

        if (windInput != 0f)
        {
            IsWindBlowing = true;
            CurrentWindDir = windInput;
            phy.Rb.angularVelocity += new Vector3(0f, 0f, windInput * windTorqueRate * jitterMultiplier * Time.fixedDeltaTime);
            phy.Rb.AddForce(new Vector3(-windInput * windPushRate * jitterMultiplier, 0f, 0f) * Time.fixedDeltaTime, ForceMode.VelocityChange);
        }

        // 2. SMOOTH AUTO GUSTS (With Turbulence)
        if (isAutoGustActive)
        {
            currentAutoGustTimer += Time.fixedDeltaTime;
            if (currentAutoGustTimer >= autoGustDuration) { isAutoGustActive = false; }
            else
            {
                IsWindBlowing = true;
                CurrentWindDir = activeGustDir;
                
                // Sine wave envelope
                float progress = currentAutoGustTimer / autoGustDuration;
                float smoothEnvelope = Mathf.Sin(progress * Mathf.PI) * (Mathf.PI / 2f);
                float stepFactor = (Time.fixedDeltaTime / autoGustDuration) * smoothEnvelope * jitterMultiplier;

                phy.Rb.angularVelocity += new Vector3(0f, 0f, activeGustDir * autoGustStrength * stepFactor);
                phy.Rb.AddForce(new Vector3(-activeGustDir * (autoGustStrength * 0.4f) * stepFactor, 0f, 0f), ForceMode.VelocityChange);
            }
        }
    }
}