// =============================================================================
// SCRIPT 2: PIDController.cs
// =============================================================================
// WHAT IT DOES: 
// Computes 3-axis corrective torque to stabilize the rocket on its trajectory.
// Specifically configured with weak control limits and low gains to mimic 
// realistic model rocket hobby servos, forcing it to visibly struggle in the wind.
// Includes individual toggles (1, 2, 3 keys) for live P, I, and D demonstrations!
// =============================================================================

using UnityEngine;

public class PIDController : MonoBehaviour
{
    [Header("Master PID Switch")]
    [Tooltip("Press S to completely toggle the PID flight computer.")]
    public bool pidActive = true;

    [Header("Individual Term Toggles")]
    public bool useP = true; // [1] Proportional: Acts like a spring (causes wobble alone)
    public bool useI = true; // [2] Integral: Memory/Trim (cancels constant wind drift)
    public bool useD = true; // [3] Derivative: Shock Absorber (dampens the wobble)

    [Header("Realistic Weak Servo Settings")]
    [Tooltip("Significantly lowered torque so recovery takes visible time!")]
    public float maxControlTorque = 1.6f; 
    
    // Lowered gains force the rocket to take several seconds to sweep back to center
    [Tooltip("Proportional Gain")] public float Kp = 2.8f; 
    [Tooltip("Integral Gain")]     public float Ki = 0.4f;
    [Tooltip("Derivative Gain")]   public float Kd = 1.0f;

    private Vector3 integralError = Vector3.zero;

    // Checks if the loop is active AND at least one term is enabled
    public bool IsActuallyActive()
    {
        return pidActive && (useP || useI || useD);
    }

    // Calculates the stabilizing torque based on cross-products and angular velocity
    public Vector3 CalculatePIDTorque(Vector3 currentNose, Vector3 targetDir, Vector3 angularVelocity)
    {
        // 1. Proportional Error (Cross product of current heading vs target heading)
        Vector3 error = Vector3.Cross(currentNose, targetDir);

        // 2. Integral Error (Accumulated drift over time)
        integralError += error * Time.fixedDeltaTime;
        integralError = Vector3.ClampMagnitude(integralError, 1.0f); // Anti-windup

        // 3. Derivative Error (Opposing angular velocity to dampen rotation)
        Vector3 derivativeError = -angularVelocity;
        
        Vector3 pidTorque = Vector3.zero;

        // Sum active terms
        if (useP) pidTorque += (Kp * error);
        if (useI) pidTorque += (Ki * integralError);
        if (useD) pidTorque += (Kd * derivativeError);

        // Clamp to physical fin/servo limits
        return Vector3.ClampMagnitude(pidTorque, maxControlTorque);
    }

    public void ToggleMasterPID() { pidActive = !pidActive; integralError = Vector3.zero; }
    public void ResetPID() { pidActive = true; useP = true; useI = true; useD = true; integralError = Vector3.zero; }
}