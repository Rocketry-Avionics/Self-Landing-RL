// =============================================================================
// SCRIPT 4: Trajectory.cs
// =============================================================================
// WHAT IT DOES: 
// Automatically draws a 3D ribbon behind the rocket to visualize the flight path.
// Syncs colors perfectly with the HUD: 
// Green = PID ON, Red = Crashed/Stalled, Brown/Orange (#D2691E) = PID OFF.
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

    void Start() { phy = GetComponent<PhyAndControls>(); pid = GetComponent<PIDController>(); SetupTrajectoryLine(); }

    void SetupTrajectoryLine()
    {
        trajectoryLine = gameObject.GetComponent<LineRenderer>();
        if (trajectoryLine == null) trajectoryLine = gameObject.AddComponent<LineRenderer>();
        trajectoryLine.startWidth = 0.035f; trajectoryLine.endWidth = 0.015f;
        trajectoryLine.material = new Material(Shader.Find("Sprites/Default"));
        trajectoryLine.positionCount = 0;
    }

    void FixedUpdate()
    {
        if (!Application.isPlaying || !phy.IsLaunched || phy.IsCrashed) return;

        UpdateTrailColor();
        pointRecordTimer += Time.fixedDeltaTime;
        
        // Record nozzle position every 0.025s
        if (pointRecordTimer >= 0.025f) {
            pointRecordTimer = 0f;
            pathPoints.Add(transform.position - phy.CurrentNoseWorld * 0.15f);
            trajectoryLine.positionCount = pathPoints.Count;
            trajectoryLine.SetPositions(pathPoints.ToArray());
        }
    }

    void UpdateTrailColor()
    {
        if (trajectoryLine == null) return;

        if (phy.IsStalled || phy.IsCrashed)
            trajectoryLine.endColor = new Color(1f, 0.2f, 0.2f); // Red
        else if (pid.IsActuallyActive())
            trajectoryLine.endColor = new Color(0f, 1f, 0f); // Green
        else
            trajectoryLine.endColor = new Color(0.82f, 0.41f, 0.12f); // Hex Match: #D2691E (Brown/Orange)
    }

    void OnRocketReset() { pathPoints.Clear(); if (trajectoryLine != null) trajectoryLine.positionCount = 0; UpdateTrailColor(); }
}