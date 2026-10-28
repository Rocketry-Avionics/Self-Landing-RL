// =============================================================================
// SCRIPT 5: HUD.cs
// =============================================================================
// WHAT THIS CODE DOES:
// This script renders the real-time Avionics Telemetry Heads-Up Display (HUD)
// in the top-left corner of the screen, plus a center-screen alert banner if
// the rocket crashes into the terrain.
// Metrics displayed:
//   - PID Loop Status (ON / OFF / STALLED / CRASHED)
//   - Velocity (m/s), Acceleration (m/s²), and Altitude (m)
//   - Trajectory Deviation Angle (θ in degrees) & Solid Angle Error (Ω in steradians)
// =============================================================================

using UnityEngine;

[RequireComponent(typeof(PhyAndControls))]
[RequireComponent(typeof(PIDController))]
public class HUD : MonoBehaviour
{
    private PhyAndControls phy;
    private PIDController pid;

    void Start()
    {
        phy = GetComponent<PhyAndControls>();
        pid = GetComponent<PIDController>();
    }

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 11;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleLeft;
        style.wordWrap = true;
        style.padding = new RectOffset(10, 10, 6, 6);
        style.normal.textColor = Color.white;

        // Determine flight state label and color coding
        string statusText;
        if (phy.IsCrashed)
            statusText = "<color=#FF2222>CRASHED INTO TERRAIN! (Press [R] to Reset)</color>";
        else if (phy.IsStalled)
            statusText = "<color=#FF3333>STALLED (>34° - Unrecoverable!)</color>";
        else if (pid.pidActive)
            statusText = "<color=#00FF00>ON (Active Stabilization)</color>";
        else
            statusText = "<color=#FF9900>OFF (Unguided Flight)</color>";

        string degColor = (phy.IsStalled || phy.IsCrashed) ? "#FF3333" : (phy.CurrentDeviationAngle > 18f ? "#FFFF00" : "#00FFFF");

        string info = $"ROCKET AVIONICS & TELEMETRY\n" +
                      $"PID Loop [S]: {statusText}\n" +
                      $"Vel: <color=#00FFFF>{phy.CurrentSpeed:F1} m/s</color>  |  Accel: <color=#00FFFF>{phy.CurrentAcceleration:F1} m/s²</color>  |  Alt: <color=#00FFFF>{phy.CurrentAltitude:F1} m</color>\n" +
                      $"Trajectory Deviation: <color={degColor}>{phy.CurrentDeviationAngle:F1}°</color>  (Solid Angle: <color={degColor}>{phy.CurrentSolidAngleSr:F3} sr</color>)\n" +
                      $"Controls: [Space] Launch  |  [D] Wind Gust (+Auto)  |  [R] Reset";

        // Main Top-Left Telemetry Box
        GUI.Box(new Rect(25, 12, 425, 95), info, style);

        // Center-Screen Alert Banner when Rocket Crashes into the Ground
        if (phy.IsCrashed)
        {
            GUIStyle crashBannerStyle = new GUIStyle(GUI.skin.box);
            crashBannerStyle.fontSize = 15;
            crashBannerStyle.fontStyle = FontStyle.Bold;
            crashBannerStyle.alignment = TextAnchor.MiddleCenter;
            crashBannerStyle.normal.textColor = Color.white;

            string crashMsg = "<color=#FF3333>TERRAIN IMPACT DETECTED — FLIGHT TERMINATED</color>\nPress <b>[R]</b> to Reset Rocket to Launchpad";
            GUI.Box(new Rect(Screen.width * 0.5f - 210f, Screen.height * 0.18f, 420f, 54f), crashMsg, crashBannerStyle);
        }
    }
}