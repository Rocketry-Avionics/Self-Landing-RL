// =============================================================================
// SCRIPT 4: Trajectory.cs
// =============================================================================
// WHAT THIS CODE DOES:
// This script renders the live 3D flight path ribbon behind the rocket in real time
// so Open Day viewers can clearly visualize the curved trajectory, wind deflections,
// and PID recovery curves.
//   - GREEN Trail: PID is ON and actively stabilizing the rocket.
//   - ORANGE Trail: PID is OFF (unguided flight).
//   - RED Trail: Rocket has exceeded 34° tilt (Aerodynamic Stall) or crashed.
// =============================================================================

using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PhyAndControls))]
[RequireComponent(typeof(PIDController))]
public class Trajectory : MonoBehaviour
{
    private PhyAndControls phy;
    private PIDController pid;
    private LineRenderer trajectoryLine;
    private List<Vector3> pathPoints = new List<Vector3>();
    private float pointRecordTimer = 0f;

    void Start()
    {
        phy = GetComponent<PhyAndControls>();
        pid = GetComponent<PIDController>();
        SetupTrajectoryLine();
    }

    // Configures the Unity LineRenderer component automatically
    void SetupTrajectoryLine()
    {
        trajectoryLine = gameObject.GetComponent<LineRenderer>();
        if (trajectoryLine == null)
        {
            trajectoryLine = gameObject.AddComponent<LineRenderer>();
        }
        trajectoryLine.startWidth = 0.035f;
        trajectoryLine.endWidth = 0.015f;
        trajectoryLine.material = new Material(Shader.Find("Sprites/Default"));
        trajectoryLine.startColor = Color.cyan;
        trajectoryLine.positionCount = 0;
    }

    // Records the rocket nozzle's 3D world position every 0.025 seconds while flying
    void FixedUpdate()
    {
        if (!Application.isPlaying || !phy.IsLaunched || phy.IsCrashed) return;

        UpdateTrailColor();

        pointRecordTimer += Time.fixedDeltaTime;
        if (pointRecordTimer >= 0.025f)
        {
            pointRecordTimer = 0f;
            pathPoints.Add(transform.position - phy.CurrentNoseWorld * 0.15f);
            trajectoryLine.positionCount = pathPoints.Count;
            trajectoryLine.SetPositions(pathPoints.ToArray());
        }
    }

    // Updates trail color dynamically based on current flight status
    void UpdateTrailColor()
    {
        if (trajectoryLine == null) return;

        if (phy.IsStalled || phy.IsCrashed)
            trajectoryLine.endColor = Color.red;
        else if (pid.pidActive)
            trajectoryLine.endColor = Color.green;
        else
            trajectoryLine.endColor = new Color(1f, 0.45f, 0f);
    }

    // Called automatically via SendMessage when the rocket launches or resets
    void OnRocketReset()
    {
        pathPoints.Clear();
        if (trajectoryLine != null) trajectoryLine.positionCount = 0;
        UpdateTrailColor();
    }
}