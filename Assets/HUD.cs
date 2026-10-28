// =============================================================================
// SCRIPT 5: HUD.cs
// =============================================================================
// WHAT THIS CODE DOES:
// Renders the real-time Avionics Telemetry Heads-Up Display on screen:
//   1. Top-Left Box: PID Controller status (ON/OFF/STALLED/CRASHED) and active
//      P, I, D terms with keyboard toggle hints.
//   2. Bottom-Left Box: Altitude, Velocity, Deviation Angle, Solid Angle (sr),
//      PID Recovery Time, and full control instructions (including F12 Screensaver).
//   3. Top-Right Banner: Flashing cyan wind warning showing gust direction.
//   4. Center Banner: Terrain impact alert when crashed.
// =============================================================================

using UnityEngine;

[RequireComponent(typeof(PhyAndControls))]
[RequireComponent(typeof(PIDController))]
[RequireComponent(typeof(WindAndInput))]
public class HUD : MonoBehaviour
{
    private PhyAndControls phy;
    private PIDController pid;
    private WindAndInput wind;

    void Start()
    {
        phy = GetComponent<PhyAndControls>();
        pid = GetComponent<PIDController>();
        wind = GetComponent<WindAndInput>();
    }

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            padding = new RectOffset(15, 15, 12, 12)
        };
        style.normal.textColor = Color.white;

        // 1. AVIONICS CONTROL LOOP BOX (Top-Left)
        string statusText;
        if (phy.IsCrashed) statusText = "<color=#FF3333>CRASHED</color>";
        else if (phy.IsStalled) statusText = "<color=#FF3333>STALLED (>38°)</color>";
        else if (!pid.IsActuallyActive()) statusText = "<color=#D2691E>OFF (Orange Track)</color>";
        else statusText = "<color=#00FF00>ON (Green Track)</color>";

        string pStr = pid.useP ? "<color=#00FF00>P</color>" : "<color=#777777>-</color>";
        string iStr = pid.useI ? "<color=#00FF00>I</color>" : "<color=#777777>-</color>";
        string dStr = pid.useD ? "<color=#00FF00>D</color>" : "<color=#777777>-</color>";

        string avionicsInfo = $"<size=16><b>AVIONICS CONTROL LOOP</b></size>\n\n" +
                              $"Status: {statusText}\n" +
                              $"Active Terms: [ {pStr}  {iStr}  {dStr} ]\n" +
                              $"<color=#AAAAAA>Keys: [S] Toggle All  |  [1, 2, 3] Toggle Terms</color>";
        GUI.Box(new Rect(25, 20, 410, 115), avionicsInfo, style);

        // 2. FLIGHT TELEMETRY BOX (Bottom-Left)
        string degColor = (phy.IsStalled || phy.IsCrashed) ? "#FF3333" : (phy.CurrentDeviationAngle > 18f ? "#FFFF00" : "#00FFFF");
        string recTimerTxt = phy.IsRecovering
            ? $"<color=#FFFF00>{phy.CurrentRecoveryTime:F2}s (Correcting...)</color>"
            : $"<color=#00FFFF>{phy.LastRecoveryTime:F2}s</color>";

        string telemetryInfo = $"<size=16><b>FLIGHT TELEMETRY</b></size>\n\n" +
                               $"<b>Alt:</b> <color=#00FFFF>{phy.CurrentAltitude:F1} m</color>   |   <b>Vel:</b> <color=#00FFFF>{phy.CurrentSpeed:F1} m/s</color>\n" +
                               $"<b>Dev Angle:</b> <color={degColor}>{phy.CurrentDeviationAngle:F1}°</color>\n" +
                               $"<b>Solid Angle:</b> <color={degColor}>{phy.CurrentSolidAngleSr:F3} sr</color>\n" +
                               $"<b>Recovery Time:</b> {recTimerTxt}\n\n" +
                               $"<color=#AAAAAA>Controls: [Space] Launch | [< >] Wind | [R] Reset | [F12] Screensaver</color>";
        GUI.Box(new Rect(25, 145, 410, 210), telemetryInfo, style);

        // 3. ANIMATED WIND INDICATOR (Top-Right)
        if (wind != null && wind.IsWindBlowing && !phy.IsCrashed)
        {
            GUIStyle windStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            windStyle.normal.textColor = Color.Lerp(Color.cyan, Color.white, Mathf.PingPong(Time.time * 8f, 1f));

            int frame = (int)(Time.time * 6f) % 3;
            string rightArrows = frame == 0 ? ">" : (frame == 1 ? ">>" : ">>>");
            string leftArrows  = frame == 0 ? "<" : (frame == 1 ? "<<" : "<<<");

            string windMsg = wind.CurrentWindDir > 0
                ? $"WIND PUSHING RIGHT  {rightArrows}"
                : $"{leftArrows}  WIND PUSHING LEFT";

            GUI.Box(new Rect(Screen.width - 380f, 20f, 350f, 55f), windMsg, windStyle);
        }

        // 4. TERRAIN IMPACT ALERT BANNER (Center)
        if (phy.IsCrashed)
        {
            GUIStyle crashStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            crashStyle.normal.textColor = Color.white;
            string crashMsg = "<color=#FF3333>TERRAIN IMPACT DETECTED — FLIGHT TERMINATED</color>\nPress <b>[R]</b> to Reset Rocket to Launchpad";
            GUI.Box(new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.2f, 480f, 70f), crashMsg, crashStyle);
        }
    }
}