// =============================================================================
// SCRIPT 1: PhyAndControls.cs
// =============================================================================
// WHAT THIS CODE DOES:
// Core physical simulation for the rocket body and procedural 3D environment:
//   1. Motor thrust along the rocket's true 3D nose vector (infinite burn for Open Day).
//   2. A 14-degree curved pitch program (gravity turn) after launch rod clearance.
//   3. Realistic 38-degree aerodynamic stall limit: heavy sustained wind can
//      overpower the PID controller, causing motor cutoff and aerodynamic stall.
//   4. Ground impact freeze: prevents tunneling below the ground on crash.
//   5. Full telemetry tracking: Altitude, Speed, Acceleration, Deviation Angle,
//      Solid Angle (steradians), and PID Recovery Timer.
//   6. Full 360-degree KSP environment (5-tier pines, snow-capped mountains,
//      clouds, and a flush concrete launchpad with a steel launch rod).
// =============================================================================

using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Rigidbody))]
public class PhyAndControls : MonoBehaviour
{
    [Header("Motor & Curved Trajectory")]
    [Tooltip("Upward thrust force produced by the rocket motor in Newtons.")]
    public float thrustInNewtons = 16f;
    [Tooltip("Set to 9999 so the rocket never runs out of fuel during Open Day.")]
    public float burnTime = 9999.0f;
    [Tooltip("Target pitch-over angle in degrees for the curved gravity-turn trajectory.")]
    public float curveAngleDegrees = 14f;
    [HideInInspector] public float currentBurnTime = 0f;

    [Header("Launch Rod Alignment")]
    [Tooltip("Aligns the chrome launch rod with the left-side launch lugs on the CAD model.")]
    public float launchRodOffsetX = -0.024f;
    public float launchRodOffsetZ = 0.0f;

    [Header("Aerodynamic Realism")]
    [Tooltip("Critical angle of attack (38 degrees). Beyond this tilt, the rocket stalls and falls.")]
    public float maxRecoveryAngle = 38f;

    // Live Telemetry Properties (Read by HUD.cs and Trajectory.cs)
    public bool IsLaunched { get; private set; } = false;
    public bool IsStalled { get; private set; } = false;
    public bool IsCrashed { get; private set; } = false;
    public float CurrentAltitude { get; private set; } = 0f;
    public float CurrentSpeed { get; private set; } = 0f;
    public float CurrentAcceleration { get; private set; } = 0f;
    public float CurrentDeviationAngle { get; private set; } = 0f;
    public float CurrentSolidAngleSr { get; private set; } = 0f;

    // PID Recovery Timer Properties
    public float CurrentRecoveryTime { get; private set; } = 0f;
    public float LastRecoveryTime { get; private set; } = 0f;
    public bool IsRecovering { get; private set; } = false;

    public Rigidbody Rb { get; private set; }
    public Vector3 CurrentNoseWorld { get; private set; }

    // Internal State
    private PIDController pid;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 localNoseDirection;
    private Vector3 previousVelocity = Vector3.zero;
    private bool hasClearedPad = false;

    // Launchpad & Exhaust References
    private Transform launchRodTransform;
    private Transform launchRodTipTransform;
    private float cachedPadTopY = 0f;
    private float cachedGrassY = 0f;
    private GameObject flameRoot;
    private Transform outerFlame;
    private Transform innerCoreFlame;

    void OnEnable()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        BuildKSPEnvironment();
    }

    void OnValidate()
    {
        UpdateLaunchRodPosition();
    }

    void Start()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        localNoseDirection = transform.InverseTransformDirection(Vector3.up);
        CurrentNoseWorld = transform.TransformDirection(localNoseDirection);

        BuildKSPEnvironment();
        if (!Application.isPlaying) return;

        burnTime = 9999f;
        maxRecoveryAngle = 38f;

        if (Camera.main != null) Camera.main.farClipPlane = 150000f;

        Rb = GetComponent<Rigidbody>();
        pid = GetComponent<PIDController>();
        Rb.isKinematic = true;
        Rb.linearDamping = 0.8f;
        Rb.angularDamping = 2.5f;

        foreach (MeshCollider mc in GetComponentsInChildren<MeshCollider>())
        {
            mc.convex = true;
        }

        Setup3DExhaustPlume();
    }

    void UpdateLaunchRodPosition()
    {
        if (launchRodTransform != null)
        {
            float rx = initialPosition.x + launchRodOffsetX;
            float rz = initialPosition.z + launchRodOffsetZ;
            launchRodTransform.localPosition = new Vector3(rx, cachedPadTopY + 0.42f, rz);
            if (launchRodTipTransform != null)
            {
                launchRodTipTransform.localPosition = new Vector3(rx, cachedPadTopY + 0.85f, rz);
            }
        }
    }

    void FixedUpdate()
    {
        if (!Application.isPlaying || !IsLaunched || IsCrashed) return;

        currentBurnTime += Time.fixedDeltaTime;

        float curveProgress = Mathf.Clamp01((currentBurnTime - 0.3f) / 3.5f);
        Vector3 targetTrajectoryDir = Quaternion.Euler(0f, 0f, -(curveProgress * curveAngleDegrees)) * Vector3.up;
        CurrentNoseWorld = transform.TransformDirection(localNoseDirection);

        // Live Telemetry Math
        CurrentAltitude = Mathf.Max(0f, transform.position.y - initialPosition.y);
        CurrentSpeed = Rb.linearVelocity.magnitude;
        CurrentAcceleration = (Rb.linearVelocity - previousVelocity).magnitude / Time.fixedDeltaTime;
        previousVelocity = Rb.linearVelocity;

        CurrentDeviationAngle = Vector3.Angle(CurrentNoseWorld, targetTrajectoryDir);
        CurrentSolidAngleSr = 2f * Mathf.PI * (1f - Mathf.Cos(CurrentDeviationAngle * Mathf.Deg2Rad));

        if (CurrentAltitude > 0.5f) hasClearedPad = true;

        // Recovery Timer Logic
        if (CurrentDeviationAngle > 2.5f && !IsRecovering && !IsStalled)
        {
            IsRecovering = true;
            CurrentRecoveryTime = 0f;
        }
        if (IsRecovering)
        {
            CurrentRecoveryTime += Time.fixedDeltaTime;
            if (CurrentDeviationAngle < 0.8f || IsStalled)
            {
                IsRecovering = false;
                LastRecoveryTime = CurrentRecoveryTime;
            }
        }

        // Ground Impact Detection
        float currentBottomY = transform.position.y - (initialPosition.y - cachedPadTopY);
        if ((hasClearedPad && currentBottomY <= cachedGrassY + 0.05f) || transform.position.y < initialPosition.y - 0.1f)
        {
            TriggerCrashLanding();
            return;
        }

        // Aerodynamic Stall Check (38 degrees)
        if (CurrentDeviationAngle > maxRecoveryAngle && currentBurnTime > 0.3f)
        {
            IsStalled = true;
        }

        // Apply Motor Thrust (Cuts off if stalled so the rocket falls to the ground!)
        if (currentBurnTime < burnTime && !IsStalled)
        {
            Rb.AddForce(CurrentNoseWorld * thrustInNewtons, ForceMode.Force);
        }

        // Virtual Launch Rod Constraint (First 0.25s)
        if (currentBurnTime < 0.25f)
        {
            Rb.MoveRotation(initialRotation);
            Rb.angularVelocity = Vector3.zero;
        }
        else
        {
            if (IsStalled)
            {
                Rb.AddTorque(Vector3.Cross(targetTrajectoryDir, CurrentNoseWorld) * 2.4f, ForceMode.Acceleration);
            }
            else if (pid != null && pid.IsActuallyActive())
            {
                Rb.AddTorque(pid.CalculatePIDTorque(CurrentNoseWorld, targetTrajectoryDir, Rb.angularVelocity), ForceMode.Acceleration);
            }
            else if (CurrentDeviationAngle > 2f)
            {
                Rb.AddTorque(Vector3.Cross(targetTrajectoryDir, CurrentNoseWorld) * 1.6f, ForceMode.Acceleration);
            }
        }
    }

    void OnCollisionEnter(Collision col)
    {
        if (IsLaunched && hasClearedPad && !IsCrashed) TriggerCrashLanding();
    }

    void TriggerCrashLanding()
    {
        IsCrashed = true;
        IsStalled = true;
        IsRecovering = false;
        CurrentAltitude = 0f;
        CurrentSpeed = 0f;
        CurrentAcceleration = 0f;

        Rb.linearVelocity = Vector3.zero;
        Rb.angularVelocity = Vector3.zero;
        Rb.isKinematic = true;

        if (flameRoot != null) flameRoot.SetActive(false);
    }

    void LateUpdate()
    {
        UpdateLaunchRodPosition();
        if (!Application.isPlaying) return;
        CurrentNoseWorld = transform.TransformDirection(localNoseDirection);
        UpdateExhaustPlumeVisuals(CurrentNoseWorld);
    }

    public void LaunchRocket()
    {
        if (IsLaunched) return;
        IsLaunched = true;
        IsCrashed = false;
        hasClearedPad = false;
        IsRecovering = false;
        Rb.isKinematic = false;
        previousVelocity = Vector3.zero;
        SendMessage("OnRocketReset", SendMessageOptions.DontRequireReceiver);
    }

    public void ResetRocket()
    {
        IsLaunched = false;
        IsStalled = false;
        IsCrashed = false;
        hasClearedPad = false;
        currentBurnTime = 0f;

        CurrentAltitude = 0f;
        CurrentSpeed = 0f;
        CurrentAcceleration = 0f;
        CurrentDeviationAngle = 0f;
        CurrentSolidAngleSr = 0f;
        CurrentRecoveryTime = 0f;
        LastRecoveryTime = 0f;
        IsRecovering = false;

        previousVelocity = Vector3.zero;
        if (Rb != null)
        {
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            Rb.isKinematic = true;
        }

        transform.position = initialPosition;
        transform.rotation = initialRotation;

        if (pid != null) pid.ResetPID();
        if (flameRoot != null) flameRoot.SetActive(false);
        SendMessage("OnRocketReset", SendMessageOptions.DontRequireReceiver);
    }

    void Setup3DExhaustPlume()
    {
        GameObject oldPlume = GameObject.Find("RocketExhaustPlume");
        if (oldPlume != null) Destroy(oldPlume);

        flameRoot = new GameObject("RocketExhaustPlume");

        GameObject outerObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(outerObj.GetComponent<Collider>());
        outerObj.transform.SetParent(flameRoot.transform, false);
        outerFlame = outerObj.transform;
        outerObj.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.38f, 0.0f, 0.88f) };

        GameObject innerObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(innerObj.GetComponent<Collider>());
        innerObj.transform.SetParent(flameRoot.transform, false);
        innerCoreFlame = innerObj.transform;
        innerObj.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.95f, 0.35f, 0.98f) };

        flameRoot.SetActive(false);
    }

    void UpdateExhaustPlumeVisuals(Vector3 noseWorld)
    {
        if (flameRoot == null) return;
        bool motorBurning = IsLaunched && !IsCrashed && !IsStalled && (currentBurnTime < burnTime);
        if (flameRoot.activeSelf != motorBurning) flameRoot.SetActive(motorBurning);

        if (motorBurning)
        {
            flameRoot.transform.position = transform.position;
            flameRoot.transform.rotation = Quaternion.LookRotation(-noseWorld);

            float f1 = Random.Range(0.16f, 0.24f);
            float f2 = Random.Range(0.09f, 0.14f);
            float w = Random.Range(0.032f, 0.042f);

            outerFlame.localScale = new Vector3(w, w, f1);
            outerFlame.localPosition = new Vector3(0f, 0f, f1 * 0.45f);
            innerCoreFlame.localScale = new Vector3(w * 0.6f, w * 0.6f, f2);
            innerCoreFlame.localPosition = new Vector3(0f, 0f, f2 * 0.4f);
        }
    }

    float GetExactRocketBottomY()
    {
        float lowestY = transform.position.y;
        bool found = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is LineRenderer || r is ParticleSystemRenderer) continue;
            if (!found || r.bounds.min.y < lowestY)
            {
                lowestY = r.bounds.min.y;
                found = true;
            }
        }
        return found ? lowestY : transform.position.y;
    }

    Material CreateEnvMat(Color col, float smoothness = 0.12f)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        Material m = new Material(sh) { color = col };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    GameObject MakePart(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        if (Application.isPlaying) Destroy(go.GetComponent<Collider>());
        else DestroyImmediate(go.GetComponent<Collider>());

        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localRotation = rot;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    [ContextMenu("Rebuild KSP Environment")]
    public void BuildKSPEnvironment()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.70f, 0.80f, 0.90f);
        RenderSettings.fogDensity = 0.0038f;

        GameObject oldPlane = GameObject.Find("Plane");
        if (oldPlane != null && oldPlane.GetComponent<Renderer>() != null) oldPlane.GetComponent<Renderer>().enabled = false;
        GameObject oldCube = GameObject.Find("Cube");
        if (oldCube != null && oldCube.GetComponent<Renderer>() != null) oldCube.GetComponent<Renderer>().enabled = false;

        float padTopY = GetExactRocketBottomY();
        cachedPadTopY = padTopY;
        float grassY = padTopY - 0.08f;
        cachedGrassY = grassY;

        GameObject existingEnv = GameObject.Find("KSP_Environment");
        if (existingEnv != null)
        {
            if (existingEnv.transform.Find("NatureDetailMarker_360") != null)
            {
                Transform rodGroupExisting = existingEnv.transform.Find("LaunchRodAssembly");
                if (rodGroupExisting != null && rodGroupExisting.childCount >= 2)
                {
                    launchRodTransform = rodGroupExisting.GetChild(0);
                    launchRodTipTransform = rodGroupExisting.GetChild(1);
                    UpdateLaunchRodPosition();
                }
                return;
            }
            if (Application.isPlaying) Destroy(existingEnv);
            else DestroyImmediate(existingEnv);
        }

        GameObject env = new GameObject("KSP_Environment");
        GameObject marker = new GameObject("NatureDetailMarker_360");
        marker.transform.SetParent(env.transform, false);

        Material grassBaseMat = CreateEnvMat(new Color(0.25f, 0.45f, 0.16f), 0.05f);
        Material grassLushMat = CreateEnvMat(new Color(0.30f, 0.50f, 0.19f), 0.05f);
        Material grassDryMat = CreateEnvMat(new Color(0.34f, 0.47f, 0.18f), 0.05f);
        Material foothillMat = CreateEnvMat(new Color(0.21f, 0.39f, 0.14f), 0.05f);
        Material shrubMat = CreateEnvMat(new Color(0.22f, 0.42f, 0.14f), 0.05f);

        Material concreteMat = CreateEnvMat(new Color(0.38f, 0.40f, 0.42f), 0.15f);
        Material darkSteelMat = CreateEnvMat(new Color(0.20f, 0.22f, 0.24f), 0.35f);
        Material chromeRodMat = CreateEnvMat(new Color(0.82f, 0.86f, 0.90f), 0.7f);
        Material clampRedMat = CreateEnvMat(new Color(0.78f, 0.15f, 0.12f), 0.3f);
        Material yellowLineMat = CreateEnvMat(new Color(0.85f, 0.72f, 0.15f), 0.2f);

        Material trunkMatDark = CreateEnvMat(new Color(0.28f, 0.18f, 0.10f), 0.05f);
        Material trunkMatWarm = CreateEnvMat(new Color(0.36f, 0.23f, 0.13f), 0.05f);
        Material pineSpruceMat = CreateEnvMat(new Color(0.11f, 0.27f, 0.10f), 0.05f);
        Material pineAlpineMat = CreateEnvMat(new Color(0.16f, 0.34f, 0.13f), 0.05f);
        Material pineCedarMat = CreateEnvMat(new Color(0.20f, 0.38f, 0.15f), 0.05f);

        Material rockDarkMat = CreateEnvMat(new Color(0.29f, 0.31f, 0.34f), 0.08f);
        Material rockMidMat = CreateEnvMat(new Color(0.37f, 0.39f, 0.42f), 0.08f);
        Material rockLightMat = CreateEnvMat(new Color(0.45f, 0.47f, 0.50f), 0.08f);
        Material snowMat = CreateEnvMat(new Color(0.94f, 0.96f, 0.99f), 0.2f);
        Material cloudMat = CreateEnvMat(new Color(0.96f, 0.98f, 1.0f), 0.05f);

        MakePart(PrimitiveType.Cube, env.transform, new Vector3(0f, grassY - 0.5f, 0f), new Vector3(100000f, 1f, 100000f), Quaternion.identity, grassBaseMat);
        MakePart(PrimitiveType.Cube, env.transform, new Vector3(initialPosition.x, padTopY - 0.04f, initialPosition.z), new Vector3(0.85f, 0.08f, 0.85f), Quaternion.identity, concreteMat);
        MakePart(PrimitiveType.Cylinder, env.transform, new Vector3(initialPosition.x, padTopY + 0.002f, initialPosition.z), new Vector3(0.26f, 0.004f, 0.26f), Quaternion.identity, darkSteelMat);
        MakePart(PrimitiveType.Cylinder, env.transform, new Vector3(initialPosition.x, padTopY + 0.012f, initialPosition.z), new Vector3(0.08f, 0.012f, 0.08f), Quaternion.identity, darkSteelMat);
        MakePart(PrimitiveType.Cube, env.transform, new Vector3(initialPosition.x, padTopY + 0.001f, initialPosition.z - 0.36f), new Vector3(0.72f, 0.004f, 0.02f), Quaternion.identity, yellowLineMat);
        MakePart(PrimitiveType.Cube, env.transform, new Vector3(initialPosition.x, padTopY + 0.001f, initialPosition.z + 0.36f), new Vector3(0.72f, 0.004f, 0.02f), Quaternion.identity, yellowLineMat);

        GameObject rodGroup = new GameObject("LaunchRodAssembly");
        rodGroup.transform.SetParent(env.transform, false);
        float rodX = initialPosition.x + launchRodOffsetX;
        float rodZ = initialPosition.z + launchRodOffsetZ;
        GameObject rodObj = MakePart(PrimitiveType.Cylinder, rodGroup.transform, new Vector3(rodX, padTopY + 0.42f, rodZ), new Vector3(0.007f, 0.42f, 0.007f), Quaternion.identity, chromeRodMat);
        GameObject tipObj = MakePart(PrimitiveType.Cube, rodGroup.transform, new Vector3(rodX, padTopY + 0.85f, rodZ), new Vector3(0.016f, 0.016f, 0.016f), Quaternion.identity, clampRedMat);
        launchRodTransform = rodObj.transform;
        launchRodTipTransform = tipObj.transform;

        Random.State oldState = Random.state;
        Random.InitState(42);

        for (int g = 0; g < 95; g++)
        {
            float gx = Random.Range(-75f, 75f);
            float gz = Random.Range(-75f, 75f);
            if (Mathf.Abs(gx) < 3.5f && Mathf.Abs(gz) < 3.5f) continue;

            Material m = (g % 3 == 0) ? grassLushMat : ((g % 3 == 1) ? grassDryMat : foothillMat);
            float gRadius = Random.Range(2.5f, 9.5f);
            MakePart(PrimitiveType.Cylinder, env.transform, new Vector3(gx, grassY + 0.003f, gz), new Vector3(gRadius, 0.006f, gRadius * Random.Range(0.7f, 1.3f)), Quaternion.Euler(0f, Random.Range(0f, 180f), 0f), m);
        }

        for (int b = 0; b < 110; b++)
        {
            float bx = Random.Range(-55f, 55f);
            float bz = Random.Range(-55f, 55f);
            if (Mathf.Abs(bx) < 3.5f && Mathf.Abs(bz) < 3.5f) continue;

            if (b % 3 == 0)
            {
                float rScale = Random.Range(0.18f, 0.55f);
                Quaternion rRot = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));
                MakePart(PrimitiveType.Cube, env.transform, new Vector3(bx, grassY + rScale * 0.25f, bz), new Vector3(rScale * 1.3f, rScale * 0.7f, rScale), rRot, rockMidMat);
                MakePart(PrimitiveType.Cube, env.transform, new Vector3(bx + rScale * 0.5f, grassY + rScale * 0.15f, bz - rScale * 0.3f), new Vector3(rScale * 0.8f, rScale * 0.5f, rScale * 0.7f), rRot, rockLightMat);
            }
            else
            {
                float sScale = Random.Range(0.22f, 0.60f);
                Vector3 sPos = new Vector3(bx, grassY + sScale * 0.25f, bz);
                Material sMat = (b % 2 == 0) ? shrubMat : grassLushMat;
                MakePart(PrimitiveType.Sphere, env.transform, sPos, new Vector3(sScale * 1.4f, sScale * 0.8f, sScale * 1.2f), Quaternion.identity, sMat);
                MakePart(PrimitiveType.Sphere, env.transform, sPos + new Vector3(sScale * 0.45f, -sScale * 0.08f, 0.1f), new Vector3(sScale * 0.95f, sScale * 0.65f, sScale * 0.9f), Quaternion.identity, pineCedarMat);
            }
        }

        for (int i = 0; i < 180; i++)
        {
            float tx = Random.Range(-75f, 75f);
            float tz = Random.Range(-75f, 75f);
            if (Mathf.Abs(tx) < 5.5f && Mathf.Abs(tz) < 5.5f) continue;

            float scale = Random.Range(0.75f, 1.85f);
            float heightStretch = Random.Range(0.9f, 1.25f);
            Vector3 tPos = new Vector3(tx, grassY, tz);

            Material tMat = (i % 3 == 0) ? pineSpruceMat : ((i % 3 == 1) ? pineAlpineMat : pineCedarMat);
            Material bMat = (i % 2 == 0) ? trunkMatDark : trunkMatWarm;

            MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, 0.08f * scale, 0f), new Vector3(0.18f * scale, 0.08f * scale, 0.18f * scale), Quaternion.identity, bMat);
            MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, 0.32f * scale * heightStretch, 0f), new Vector3(0.11f * scale, 0.32f * scale * heightStretch, 0.11f * scale), Quaternion.identity, bMat);

            float[] tH = { 0.52f, 0.78f, 1.02f, 1.24f, 1.42f };
            float[] tW = { 0.74f, 0.60f, 0.46f, 0.33f, 0.18f };
            float[] tT = { 0.20f, 0.19f, 0.18f, 0.16f, 0.15f };
            for (int t = 0; t < 5; t++)
            {
                MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, tH[t] * scale * heightStretch, 0f), new Vector3(tW[t] * scale, tT[t] * scale * heightStretch, tW[t] * scale), Quaternion.identity, tMat);
            }
        }

        int numFoothills = 28;
        for (int h = 0; h < numFoothills; h++)
        {
            float angle = (h / (float)numFoothills) * Mathf.PI * 2f + Random.Range(-0.08f, 0.08f);
            float dist = Random.Range(52f, 75f);
            float hx = Mathf.Sin(angle) * dist;
            float hz = Mathf.Cos(angle) * dist;
            float hScale = Random.Range(15f, 28f);
            MakePart(PrimitiveType.Sphere, env.transform, new Vector3(hx, grassY - hScale * 0.28f, hz), new Vector3(hScale * 1.9f, hScale * 0.68f, hScale * 1.5f), Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f), (h % 2 == 0) ? foothillMat : grassBaseMat);
        }

        int numPeaks = 36;
        for (int i = 0; i < numPeaks; i++)
        {
            float angle = (i / (float)numPeaks) * Mathf.PI * 2f + Random.Range(-0.05f, 0.05f);
            float dist = Random.Range(85f, 115f);
            float mx = Mathf.Sin(angle) * dist;
            float mz = Mathf.Cos(angle) * dist;
            float mHeight = Random.Range(22f, 48f);
            float mWidth = Random.Range(18f, 30f);

            Quaternion mRot = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
            Material mainRock = (i % 3 == 0) ? rockDarkMat : ((i % 3 == 1) ? rockMidMat : rockLightMat);

            MakePart(PrimitiveType.Cube, env.transform, new Vector3(mx, grassY + mHeight * 0.40f, mz), new Vector3(mWidth, mHeight, mWidth * 0.85f), mRot, mainRock);
            MakePart(PrimitiveType.Cube, env.transform, new Vector3(mx, grassY + mHeight * 0.85f, mz), new Vector3(mWidth * 0.80f, mHeight * 0.25f, mWidth * 0.74f), mRot, snowMat);
        }

        for (int c = 0; c < 36; c++)
        {
            float cx = Random.Range(-85f, 85f);
            float cy = Random.Range(10f, 65f);
            float cz = Random.Range(-85f, 85f);
            float cScale = Random.Range(1.4f, 3.2f);
            Vector3 cPos = new Vector3(cx, cy, cz);

            MakePart(PrimitiveType.Sphere, env.transform, cPos, new Vector3(cScale * 2.4f, cScale * 0.72f, cScale * 1.3f), Quaternion.identity, cloudMat);
            MakePart(PrimitiveType.Sphere, env.transform, cPos + new Vector3(-cScale * 0.8f, -cScale * 0.1f, 0.1f), new Vector3(cScale * 1.6f, cScale * 0.52f, cScale * 1.1f), Quaternion.identity, cloudMat);
            MakePart(PrimitiveType.Sphere, env.transform, cPos + new Vector3(cScale * 0.85f, -cScale * 0.08f, -0.1f), new Vector3(cScale * 1.7f, cScale * 0.58f, cScale * 1.1f), Quaternion.identity, cloudMat);
        }

        Random.state = oldState;
    }
}