// =============================================================================
// SCRIPT 2: PIDController.cs
// =============================================================================
// WHAT THIS CODE DOES:
// This script acts as the onboard Flight Computer (Avionics Control Loop).
// Every physics frame, it takes the rocket's current nose direction and compares
// it against the desired trajectory vector using a 3D vector cross product.
// It computes corrective torque using three mathematical terms:
//   - Proportional (Kp): Steers against the current angular error.
//   - Integral (Ki): Eliminates persistent steady-state drift over time.
//   - Derivative (Kd): Applies damping against angular velocity to prevent overshoot.
// The gains (Kp = 6.8, Ki = 0.75, Kd = 2.2) are specifically tuned so the rocket
// takes a smooth, realistic ~2 to 2.5 seconds to recover from a wind gust.
// =============================================================================

using UnityEngine;

public class PIDController : MonoBehaviour
{
    [Header("PID Status & Tuned Weights (RL Target)")]
    [Tooltip("Whether active PID stabilization is currently engaged (toggled with S key).")]
    public bool pidActive = true;

    [Tooltip("Maximum physical torque the fins/TVC can exert (clamps maximum authority).")]
    public float maxControlTorque = 5.2f; // Slightly softer authority for a more gradual recovery

    [Tooltip("Proportional Gain: Controls how strongly the rocket steers back toward target angle.")]
    public float Kp = 6.8f; // Tuned down slightly from 8.5 so recovery takes slightly longer

    [Tooltip("Integral Gain: Accumulates past error to cancel out steady off-axis drift.")]
    public float Ki = 0.75f;

    [Tooltip("Derivative Gain: Dampens rotation speed so the nose settles smoothly without violent oscillation.")]
    public float Kd = 2.2f;

    // Stores accumulated angular error over time for the Integral (Ki) term
    private Vector3 integralError = Vector3.zero;

    /// <summary>
    /// Computes the 3-axis corrective torque vector needed to align currentNose with targetDir.
    /// </summary>
    public Vector3 CalculatePIDTorque(Vector3 currentNose, Vector3 targetDir, Vector3 angularVelocity)
    {
        // 1. Proportional Error: Cross product gives the rotation axis and sine of angle error
        Vector3 error = Vector3.Cross(currentNose, targetDir);

        // 2. Integral Error: Accumulate error over fixed timestep (with anti-windup clamping)
        integralError += error * Time.fixedDeltaTime;
        integralError = Vector3.ClampMagnitude(integralError, 1.0f);

        // 3. Derivative Error: Oppose current angular rotation rate to prevent overshoot
        Vector3 derivativeError = -angularVelocity;

        // Combine P, I, and D terms and clamp to physical control surface limits
        Vector3 pidTorque = (Kp * error) + (Ki * integralError) + (Kd * derivativeError);
        return Vector3.ClampMagnitude(pidTorque, maxControlTorque);
    }

    /// <summary>
    /// Toggles the PID controller ON or OFF when the user presses the S key.
    /// </summary>
    public void TogglePID()
    {
        pidActive = !pidActive;
        integralError = Vector3.zero;
    }

    /// <summary>
    /// Resets the PID controller state when the rocket is reset to the launchpad.
    /// </summary>
    public void ResetPID()
    {
        pidActive = true;
        integralError = Vector3.zero;
    }
}