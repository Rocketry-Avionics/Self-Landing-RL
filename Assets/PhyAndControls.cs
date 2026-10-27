// =============================================================================
// SCRIPT 1: PhyAndControls.cs
// =============================================================================
// WHAT THIS CODE DOES:
// This is the core physical simulation script for the rocket body. It manages:
//   1. Motor thrust application along the rocket's true 3D nose vector.
//   2. A 14-degree curved pitch program (gravity turn) after launch rod clearance.
//   3. Aerodynamic stall detection (if tilted past 34 degrees, drag overwhelms fins).
//   4. Ground impact detection: stops the rocket from ever clipping below the ground,
//      freezes flight upon impact, and plays a simple 3D fireball/smoke crash animation.
//   5. Live telemetry calculations (Altitude, Velocity, Acceleration, Deviation Angle,
//      and Solid Angle in steradians) for the HUD and Trajectory scripts.
//   6. Procedural generation of the clean launchpad, built-in lug launch rod,
//      3D motor exhaust plume, and detailed natural valley/mountain environment.
// =============================================================================

using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Rigidbody))]
public class PhyAndControls : MonoBehaviour
{
    [Header("Motor & Curved Trajectory Settings")]
    [Tooltip("Upward thrust force produced by the rocket motor in Newtons.")]
    public float thrustInNewtons = 16f;
    [Tooltip("Duration in seconds that the rocket motor burns before burnout.")]
    public float burnTime = 25.0f;
    [Tooltip("Target pitch-over angle in degrees for the curved gravity-turn trajectory.")]
    public float curveAngleDegrees = 14f;
    [HideInInspector] public float currentBurnTime = 0f;

    [Header("Launch Rod Alignment (Threads Built-in CAD Lugs)")]
    [Tooltip("Adjust X/Z in Inspector to align the chrome rod with the 3D rocket's left-side launch lugs.")]
    public float launchRodOffsetX = -0.024f;
    public float launchRodOffsetZ = 0.0f;

    [Header("Aerodynamic Realism Limits")]
    [Tooltip("Critical angle of attack (degrees). Beyond this tilt, aerodynamic stall occurs.")]
    public float maxRecoveryAngle = 34f;

    // -------------------------------------------------------------------------
    // Public Live Telemetry Properties (Read by HUD.cs and Trajectory.cs)
    // -------------------------------------------------------------------------
    public bool IsLaunched { get; private set; } = false;
    public bool IsStalled { get; private set; } = false;
    public bool IsCrashed { get; private set; } = false;
    public float CurrentAltitude { get; private set; } = 0f;
    public float CurrentSpeed { get; private set; } = 0f;
    public float CurrentAcceleration { get; private set; } = 0f;
    public float CurrentDeviationAngle { get; private set; } = 0f;
    public float CurrentSolidAngleSr { get; private set; } = 0f;

    public Rigidbody Rb { get; private set; }
    public Vector3 CurrentNoseWorld { get; private set; }

    // Internal references and state variables
    private PIDController pid;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 localNoseDirection;
    private Vector3 previousVelocity = Vector3.zero;
    private bool hasClearedPad = false; // Tracks when rocket has climbed above 0.5m

    private Transform launchRodTransform;
    private Transform launchRodTipTransform;
    private float cachedPadTopY = 0f;
    private float cachedGrassY = 0f;

    // 3D Exhaust Plume Objects
    private GameObject flameRoot;
    private Transform outerFlame;
    private Transform innerCoreFlame;

    // Basic Crash Animation Objects
    private GameObject crashEffectRoot;
    private Transform crashFireball;
    private Transform crashSmokeRing;
    private float crashAnimTimer = 0f;

    // Runs automatically in Edit Mode so the environment is visible before hitting Play
    void OnEnable()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        BuildKSPEnvironment();
    }

    // Updates launch rod alignment live when sliders are moved in the Inspector
    void OnValidate()
    {
        UpdateLaunchRodPosition();
    }

    void Start()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        // Determine which local axis of the imported CAD model points straight up
        localNoseDirection = transform.InverseTransformDirection(Vector3.up);
        CurrentNoseWorld = transform.TransformDirection(localNoseDirection);

        BuildKSPEnvironment();

        if (!Application.isPlaying) return;

        Rb = GetComponent<Rigidbody>();
        pid = GetComponent<PIDController>();

        // Keep rocket locked on the pad until Spacebar is pressed
        Rb.isKinematic = true;
        Rb.linearDamping = 0.8f;
        Rb.angularDamping = 2.5f;

        // Ensure all child mesh colliders are convex so Unity 6 physics doesn't error
        MeshCollider[] meshColliders = GetComponentsInChildren<MeshCollider>();
        foreach (MeshCollider mc in meshColliders)
        {
            mc.convex = true;
        }

        Setup3DExhaustPlume();
        SetupCrashVisuals();
    }

    // Moves the launch rod smoothly if offsets are adjusted
    void UpdateLaunchRodPosition()
    {
        if (launchRodTransform != null)
        {
            float rodX = initialPosition.x + launchRodOffsetX;
            float rodZ = initialPosition.z + launchRodOffsetZ;
            launchRodTransform.localPosition = new Vector3(rodX, cachedPadTopY + 0.42f, rodZ);
            if (launchRodTipTransform != null)
            {
                launchRodTipTransform.localPosition = new Vector3(rodX, cachedPadTopY + 0.85f, rodZ);
            }
        }
    }

    // Deterministic physics loop (runs at fixed time steps for accurate control math)
    void FixedUpdate()
    {
        // Stop all physics updates if not launched or if the rocket has crashed!
        if (!Application.isPlaying || !IsLaunched || IsCrashed) return;

        currentBurnTime += Time.fixedDeltaTime;

        // Calculate the target curved trajectory vector (smoothly pitches to 14° over 3.5s)
        float curveProgress = Mathf.Clamp01((currentBurnTime - 0.3f) / 3.5f);
        float currentTargetAngle = curveProgress * curveAngleDegrees;
        Vector3 targetTrajectoryDir = Quaternion.Euler(0f, 0f, -currentTargetAngle) * Vector3.up;

        CurrentNoseWorld = transform.TransformDirection(localNoseDirection);

        // ---------------------------------------------------------------------
        // Live Telemetry Calculations
        // ---------------------------------------------------------------------
        CurrentAltitude = Mathf.Max(0f, transform.position.y - initialPosition.y);
        CurrentSpeed = Rb.linearVelocity.magnitude;
        CurrentAcceleration = (Rb.linearVelocity - previousVelocity).magnitude / Time.fixedDeltaTime;
        previousVelocity = Rb.linearVelocity;

        // Calculate Planar Deviation Angle (degrees) and Solid Angle Cone Error (steradians)
        CurrentDeviationAngle = Vector3.Angle(CurrentNoseWorld, targetTrajectoryDir);
        float thetaRad = CurrentDeviationAngle * Mathf.Deg2Rad;
        CurrentSolidAngleSr = 2f * Mathf.PI * (1f - Mathf.Cos(thetaRad));

        // Mark that the rocket has safely left the launchpad once it climbs past 0.5 meters
        if (CurrentAltitude > 0.5f)
        {
            hasClearedPad = true;
        }

        // ---------------------------------------------------------------------
        // Ground Impact / Anti-Tunneling Check (Prevents going below ground!)
        // ---------------------------------------------------------------------
        float currentBottomY = transform.position.y - (initialPosition.y - cachedPadTopY);
        if ((hasClearedPad && currentBottomY <= cachedGrassY + 0.05f) || transform.position.y < initialPosition.y - 0.1f)
        {
            TriggerCrashLanding();
            return;
        }

        // Check if rocket tilt has exceeded the aerodynamic stall threshold (34°)
        if (CurrentDeviationAngle > maxRecoveryAngle && currentBurnTime > 0.3f)
        {
            IsStalled = true;
        }

        // 1. Apply Motor Thrust along the rocket's nose direction
        if (currentBurnTime < burnTime)
        {
            Rb.AddForce(CurrentNoseWorld * thrustInNewtons, ForceMode.Force);
        }

        // 2. Virtual Launch Rod Constraint (keeps rocket straight during first 0.25s on rail)
        if (currentBurnTime < 0.25f)
        {
            Rb.MoveRotation(initialRotation);
            Rb.angularVelocity = Vector3.zero;
        }
        // 3. Free-Flight Aerodynamics & PID Stabilization
        else
        {
            if (IsStalled)
            {
                // Past 34 degrees, aerodynamic drag tips the rocket further off-axis
                Vector3 stallTipTorque = Vector3.Cross(targetTrajectoryDir, CurrentNoseWorld);
                Rb.AddTorque(stallTipTorque * 2.4f, ForceMode.Acceleration);
            }
            else if (pid != null && pid.pidActive)
            {
                // Request corrective torque from the PIDController brain
                Vector3 pidTorque = pid.CalculatePIDTorque(CurrentNoseWorld, targetTrajectoryDir, Rb.angularVelocity);
                Rb.AddTorque(pidTorque, ForceMode.Acceleration);
            }
            else
            {
                // When PID is OFF: natural aerodynamic instability causes gradual drift
                if (CurrentDeviationAngle > 2f)
                {
                    Vector3 driftTorque = Vector3.Cross(targetTrajectoryDir, CurrentNoseWorld);
                    Rb.AddTorque(driftTorque * 1.6f, ForceMode.Acceleration);
                }
            }
        }
    }

    // backup collision check if rocket hits any solid collider after clearing pad
    void OnCollisionEnter(Collision collision)
    {
        if (IsLaunched && hasClearedPad && !IsCrashed)
        {
            TriggerCrashLanding();
        }
    }

    // =========================================================================
    // GROUND IMPACT STOP & BASIC CRASH ANIMATION
    // =========================================================================
    void TriggerCrashLanding()
    {
        IsCrashed = true;
        IsStalled = true;
        CurrentAltitude = 0f;
        CurrentSpeed = 0f;
        CurrentAcceleration = 0f;

        // 1. Immediately freeze physics so the rocket NEVER falls below the ground!
        Rb.linearVelocity = Vector3.zero;
        Rb.angularVelocity = Vector3.zero;
        Rb.isKinematic = true;

        // 2. Clamp rocket position right onto the ground surface and tilt it as a wrecked rocket
        Vector3 clampedPos = transform.position;
        clampedPos.y = Mathf.Max(clampedPos.y, initialPosition.y - 0.04f);
        transform.position = clampedPos;
        transform.rotation = Quaternion.Euler(0f, 0f, 72f) * initialRotation;

        // 3. Shut off motor exhaust flame
        if (flameRoot != null) flameRoot.SetActive(false);

        // 4. Start the basic 3D crash fireball/smoke animation at the impact point
        if (crashEffectRoot != null)
        {
            crashEffectRoot.transform.position = clampedPos;
            crashEffectRoot.SetActive(true);
            crashAnimTimer = 0f;
        }
    }

    void SetupCrashVisuals()
    {
        GameObject oldCrash = GameObject.Find("RocketCrashEffect");
        if (oldCrash != null) Destroy(oldCrash);

        crashEffectRoot = new GameObject("RocketCrashEffect");

        // Inner orange-red impact fireball sphere
        GameObject fireObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(fireObj.GetComponent<Collider>());
        fireObj.transform.SetParent(crashEffectRoot.transform, false);
        crashFireball = fireObj.transform;
        Renderer fireRen = fireObj.GetComponent<Renderer>();
        fireRen.material = new Material(Shader.Find("Sprites/Default"));
        fireRen.material.color = new Color(1f, 0.35f, 0.05f, 0.9f);

        // Outer dark-grey expanding dust/smoke ring
        GameObject smokeObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(smokeObj.GetComponent<Collider>());
        smokeObj.transform.SetParent(crashEffectRoot.transform, false);
        crashSmokeRing = smokeObj.transform;
        Renderer smokeRen = smokeObj.GetComponent<Renderer>();
        smokeRen.material = new Material(Shader.Find("Sprites/Default"));
        smokeRen.material.color = new Color(0.22f, 0.22f, 0.22f, 0.75f);

        crashEffectRoot.SetActive(false);
    }

    void UpdateCrashAnimation()
    {
        if (!IsCrashed || crashEffectRoot == null || !crashEffectRoot.activeSelf) return;

        crashAnimTimer += Time.deltaTime;

        // Simple 1.5-second expanding fireball + lingering smoke puff animation
        float t = Mathf.Clamp01(crashAnimTimer / 1.2f);
        float fireScale = Mathf.Lerp(0.15f, 1.35f, Mathf.Sin(t * Mathf.PI));
        float smokeScale = Mathf.Lerp(0.2f, 1.9f, t);

        if (crashFireball != null)
        {
            crashFireball.localScale = new Vector3(fireScale, fireScale * 0.8f, fireScale);
            // Flicker color between bright yellow-orange and dark red
            Renderer r = crashFireball.GetComponent<Renderer>();
            if (r != null)
            {
                r.material.color = Color.Lerp(new Color(1f, 0.65f, 0.1f, 0.95f), new Color(0.8f, 0.15f, 0.05f, 0.0f), t);
            }
        }

        if (crashSmokeRing != null)
        {
            crashSmokeRing.localPosition = new Vector3(0f, t * 0.45f, 0f);
            crashSmokeRing.localScale = new Vector3(smokeScale * 1.3f, smokeScale * 0.6f, smokeScale * 1.3f);
            Renderer sr = crashSmokeRing.GetComponent<Renderer>();
            if (sr != null)
            {
                sr.material.color = Color.Lerp(new Color(0.25f, 0.25f, 0.25f, 0.8f), new Color(0.15f, 0.15f, 0.15f, 0.0f), Mathf.Clamp01(crashAnimTimer / 2.0f));
            }
        }
    }

    void LateUpdate()
    {
        UpdateLaunchRodPosition();
        if (!Application.isPlaying) return;
        CurrentNoseWorld = transform.TransformDirection(localNoseDirection);
        UpdateExhaustPlumeVisuals(CurrentNoseWorld);
        UpdateCrashAnimation();
    }

    // Triggered by WindAndInput when Spacebar is pressed
    public void LaunchRocket()
    {
        if (IsLaunched) return;
        IsLaunched = true;
        IsCrashed = false;
        hasClearedPad = false;
        Rb.isKinematic = false;
        previousVelocity = Vector3.zero;
        SendMessage("OnRocketReset", SendMessageOptions.DontRequireReceiver);
    }

    // Triggered by WindAndInput when R is pressed
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
        previousVelocity = Vector3.zero;

        Rb.linearVelocity = Vector3.zero;
        Rb.angularVelocity = Vector3.zero;
        Rb.isKinematic = true;

        transform.position = initialPosition;
        transform.rotation = initialRotation;

        if (pid != null) pid.ResetPID();
        if (flameRoot != null) flameRoot.SetActive(false);
        if (crashEffectRoot != null) crashEffectRoot.SetActive(false);

        SendMessage("OnRocketReset", SendMessageOptions.DontRequireReceiver);
    }

    // =========================================================================
    // 3D DYNAMIC MOTOR EXHAUST PLUME
    // =========================================================================
    void Setup3DExhaustPlume()
    {
        GameObject oldPlume = GameObject.Find("RocketExhaustPlume");
        if (oldPlume != null) Destroy(oldPlume);

        flameRoot = new GameObject("RocketExhaustPlume");

        GameObject outerObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(outerObj.GetComponent<Collider>());
        outerObj.transform.SetParent(flameRoot.transform, false);
        outerFlame = outerObj.transform;

        Renderer outerRen = outerObj.GetComponent<Renderer>();
        outerRen.material = new Material(Shader.Find("Sprites/Default"));
        outerRen.material.color = new Color(1f, 0.38f, 0.0f, 0.88f);

        GameObject innerObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(innerObj.GetComponent<Collider>());
        innerObj.transform.SetParent(flameRoot.transform, false);
        innerCoreFlame = innerObj.transform;

        Renderer innerRen = innerObj.GetComponent<Renderer>();
        innerRen.material = new Material(Shader.Find("Sprites/Default"));
        innerRen.material.color = new Color(1f, 0.95f, 0.35f, 0.98f);

        flameRoot.SetActive(false);
    }

    void UpdateExhaustPlumeVisuals(Vector3 noseWorld)
    {
        if (flameRoot == null) return;

        bool motorBurning = IsLaunched && !IsCrashed && (currentBurnTime < burnTime);
        if (flameRoot.activeSelf != motorBurning)
        {
            flameRoot.SetActive(motorBurning);
        }

        if (motorBurning)
        {
            flameRoot.transform.position = transform.position;
            flameRoot.transform.rotation = Quaternion.LookRotation(-noseWorld);

            float flicker1 = Random.Range(0.16f, 0.24f);
            float flicker2 = Random.Range(0.09f, 0.14f);
            float widthJitter = Random.Range(0.032f, 0.042f);

            outerFlame.localScale = new Vector3(widthJitter, widthJitter, flicker1);
            outerFlame.localPosition = new Vector3(0f, 0f, flicker1 * 0.45f);

            innerCoreFlame.localScale = new Vector3(widthJitter * 0.6f, widthJitter * 0.6f, flicker2);
            innerCoreFlame.localPosition = new Vector3(0f, 0f, flicker2 * 0.4f);
        }
    }

    // =========================================================================
    // SIMPLE PAD + DETAILED NATURE ENVIRONMENT (GRASS, TREES, MOUNTAINS)
    // =========================================================================
    float GetExactRocketBottomY()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        float lowestY = transform.position.y;
        bool found = false;
        foreach (Renderer r in renderers)
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
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        Material m = new Material(sh);
        m.color = col;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    GameObject MakePart(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        if (Application.isPlaying)
            Destroy(go.GetComponent<Collider>());
        else
            DestroyImmediate(go.GetComponent<Collider>());

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
        if (oldPlane != null && oldPlane.GetComponent<Renderer>() != null)
            oldPlane.GetComponent<Renderer>().enabled = false;
        GameObject oldCube = GameObject.Find("Cube");
        if (oldCube != null && oldCube.GetComponent<Renderer>() != null)
            oldCube.GetComponent<Renderer>().enabled = false;

        float padTopY = GetExactRocketBottomY();
        cachedPadTopY = padTopY;
        float grassY = padTopY - 0.08f;
        cachedGrassY = grassY;

        GameObject existingEnv = GameObject.Find("KSP_Environment");
        if (existingEnv != null)
        {
            if (existingEnv.transform.Find("NatureDetailMarker_v3") != null)
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
        GameObject marker = new GameObject("NatureDetailMarker_v3");
        marker.transform.SetParent(env.transform, false);

        // Nature & Pad Materials
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

        // 1. Simple Launchpad + Left-Side Launch Rod (for built-in lugs)
        MakePart(PrimitiveType.Cube, env.transform, new Vector3(0f, grassY - 0.5f, 0f), new Vector3(1500f, 1f, 1500f), Quaternion.identity, grassBaseMat);

        Vector3 padCenter = new Vector3(initialPosition.x, padTopY - 0.04f, initialPosition.z);
        MakePart(PrimitiveType.Cube, env.transform, padCenter, new Vector3(0.85f, 0.08f, 0.85f), Quaternion.identity, concreteMat);
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

        // 2. Detailed Meadow Grass Mounds, Tufts & Boulders
        Random.State oldState = Random.state;
        Random.InitState(42);

        for (int g = 0; g < 65; g++)
        {
            float gx = Random.Range(-55f, 65f);
            float gz = Random.Range(-8f, 80f);
            if (Mathf.Abs(gx) < 2.2f && Mathf.Abs(gz) < 2.2f) continue;

            float gRadius = Random.Range(2.5f, 9.5f);
            Material m = (g % 3 == 0) ? grassLushMat : ((g % 3 == 1) ? grassDryMat : foothillMat);
            MakePart(PrimitiveType.Cylinder, env.transform, new Vector3(gx, grassY + 0.003f, gz), new Vector3(gRadius, 0.006f, gRadius * Random.Range(0.7f, 1.3f)), Quaternion.Euler(0f, Random.Range(0f, 180f), 0f), m);
        }

        for (int b = 0; b < 90; b++)
        {
            float bx = Random.Range(-35f, 45f);
            float bz = Random.Range(-3f, 60f);
            if (Mathf.Abs(bx) < 2.0f && Mathf.Abs(bz) < 2.5f) continue;

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

        // 3. Detailed 5-Tier Pine & Spruce Trees
        for (int i = 0; i < 125; i++)
        {
            float tx = Random.Range(-60f, 80f);
            float tz = Random.Range(-4f, 92f);
            if (Mathf.Abs(tx) < 3.8f && Mathf.Abs(tz) < 4.5f) continue;

            float scale = Random.Range(0.75f, 1.85f);
            float heightStretch = Random.Range(0.9f, 1.25f);
            Vector3 tPos = new Vector3(tx, grassY, tz);

            Material tMat = (i % 3 == 0) ? pineSpruceMat : ((i % 3 == 1) ? pineAlpineMat : pineCedarMat);
            Material bMat = (i % 2 == 0) ? trunkMatDark : trunkMatWarm;

            MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, 0.08f * scale, 0f), new Vector3(0.18f * scale, 0.08f * scale, 0.18f * scale), Quaternion.identity, bMat);
            MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, 0.32f * scale * heightStretch, 0f), new Vector3(0.11f * scale, 0.32f * scale * heightStretch, 0.11f * scale), Quaternion.identity, bMat);

            float[] tierHeights = { 0.52f, 0.78f, 1.02f, 1.24f, 1.42f };
            float[] tierWidths  = { 0.74f, 0.60f, 0.46f, 0.33f, 0.18f };
            float[] tierThick   = { 0.20f, 0.19f, 0.18f, 0.16f, 0.15f };

            for (int t = 0; t < 5; t++)
            {
                float yH = tierHeights[t] * scale * heightStretch;
                float w = tierWidths[t] * scale;
                float h = tierThick[t] * scale * heightStretch;
                MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, yH, 0f), new Vector3(w, h, w), Quaternion.identity, tMat);
            }
        }

        // 4. Layered Foothills & Complex Snow-Capped Mountains
        for (int h = 0; h < 18; h++)
        {
            float hx = Mathf.Lerp(-95f, 115f, h / 17f) + Random.Range(-4f, 4f);
            float hz = Random.Range(38f, 62f);
            float hScale = Random.Range(15f, 28f);
            Material hMat = (h % 2 == 0) ? foothillMat : grassBaseMat;
            MakePart(PrimitiveType.Sphere, env.transform, new Vector3(hx, grassY - hScale * 0.26f, hz), new Vector3(hScale * 1.9f, hScale * 0.72f, hScale * 1.3f), Quaternion.identity, hMat);
        }

        int numPeaks = 26;
        for (int i = 0; i < numPeaks; i++)
        {
            float t = (float)i / (numPeaks - 1);
            float mx = Mathf.Lerp(-120f, 140f, t);
            float mz = Mathf.Lerp(98f, 70f, t) + Random.Range(-7f, 7f);
            float mHeight = Mathf.Lerp(18f, 48f, t) + Random.Range(-4f, 6f);
            float mWidth = Random.Range(18f, 30f);

            Quaternion mRot = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(15f, 65f), Random.Range(-12f, 12f));
            Material mainRock = (i % 3 == 0) ? rockDarkMat : ((i % 3 == 1) ? rockMidMat : rockLightMat);

            MakePart(PrimitiveType.Cube, env.transform, new Vector3(mx, grassY + mHeight * 0.40f, mz), new Vector3(mWidth, mHeight, mWidth * 0.85f), mRot, mainRock);

            Quaternion subRot = Quaternion.Euler(Random.Range(-8f, 12f), Random.Range(25f, 75f), Random.Range(-18f, 18f));
            float subW = mWidth * 0.75f;
            float subH = mHeight * 0.68f;
            MakePart(PrimitiveType.Cube, env.transform, new Vector3(mx + mWidth * 0.42f, grassY + subH * 0.38f, mz - 3.5f), new Vector3(subW, subH, subW * 0.8f), subRot, rockDarkMat);

            MakePart(PrimitiveType.Cube, env.transform, new Vector3(mx, grassY + mHeight * 0.85f, mz - 0.5f), new Vector3(mWidth * 0.80f, mHeight * 0.25f, mWidth * 0.74f), mRot, snowMat);
            if (mHeight > 26f)
            {
                MakePart(PrimitiveType.Cube, env.transform, new Vector3(mx + mWidth * 0.38f, grassY + subH * 0.82f, mz - 3.6f), new Vector3(subW * 0.72f, subH * 0.20f, subW * 0.68f), subRot, snowMat);
            }
        }

        // 5. High-Altitude Cumulus Cloud Clusters
        for (int c = 0; c < 28; c++)
        {
            float cx = Random.Range(-60f, 95f);
            float cy = Random.Range(8f, 70f);
            float cz = Random.Range(14f, 68f);
            float cScale = Random.Range(1.4f, 3.2f);
            Vector3 cPos = new Vector3(cx, cy, cz);

            MakePart(PrimitiveType.Sphere, env.transform, cPos, new Vector3(cScale * 2.4f, cScale * 0.72f, cScale * 1.3f), Quaternion.identity, cloudMat);
            MakePart(PrimitiveType.Sphere, env.transform, cPos + new Vector3(-cScale * 0.8f, -cScale * 0.1f, 0.1f), new Vector3(cScale * 1.6f, cScale * 0.52f, cScale * 1.1f), Quaternion.identity, cloudMat);
            MakePart(PrimitiveType.Sphere, env.transform, cPos + new Vector3(cScale * 0.85f, -cScale * 0.08f, -0.1f), new Vector3(cScale * 1.7f, cScale * 0.58f, cScale * 1.1f), Quaternion.identity, cloudMat);
        }

        Random.state = oldState;
    }
}