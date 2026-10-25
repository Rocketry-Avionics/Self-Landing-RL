using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[ExecuteAlways]
public class RocketFlight : MonoBehaviour
{
    [Header("Launch Rod Alignment (Threads Through Built-In Lugs!)")]
    public float launchRodOffsetX = -0.024f; // Negative X puts the rod on the LEFT side where your model's lugs are!
    public float launchRodOffsetZ = 0.0f;    // Tweak slightly in Inspector if lugs are slightly forward/back

    [Header("Motor & Curved Trajectory")]
    public float thrustInNewtons = 16f;
    public float burnTime = 25.0f;
    public float curveAngleDegrees = 14f; 
    private float currentBurnTime = 0f;

    [Header("Disturbance (D Key - 3 Rapid Presses Overwhelms PID)")]
    public float disturbanceKickSpeed = 2.15f; 
    public float disturbanceSidePush = 0.85f; 
    private float lastDisturbDir = 1f;
    private float lastDisturbTime = -10f;

    [Header("PID Controller & Realism Limits (S Key)")]
    public bool pidActive = true;
    public float maxRecoveryAngle = 34f; // Stalls past 34 degrees!
    public float maxControlTorque = 6.5f; 
    public float Kp = 8.5f;               
    public float Ki = 0.8f;
    public float Kd = 2.6f;

    private Rigidbody rb;
    private bool isLaunched = false;
    private bool isStalled = false;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 localNoseDirection;
    private Vector3 integralError = Vector3.zero;

    // Live Telemetry Data
    private Vector3 previousVelocity = Vector3.zero;
    private float currentSpeed = 0f;
    private float currentAcceleration = 0f;
    private float currentDeviationAngle = 0f;
    private float currentSolidAngleSr = 0f;

    // Visuals: Trajectory Line & 3D Exhaust Plume
    private LineRenderer trajectoryLine;
    private List<Vector3> pathPoints = new List<Vector3>();
    private float pointRecordTimer = 0f;

    private GameObject flameRoot;
    private Transform outerFlame;
    private Transform innerCoreFlame;
    private Transform launchRodTransform;
    private Transform launchRodCapTransform;

    void OnEnable()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        BuildKSPEnvironment();
    }

    void OnValidate()
    {
        // Live-updates the rod position in the Scene view if you tweak launchRodOffsetX/Z in the Inspector!
        UpdateLaunchRodPosition();
    }

    void Start()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        localNoseDirection = transform.InverseTransformDirection(Vector3.up);

        BuildKSPEnvironment();

        if (!Application.isPlaying) return;

        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;

        rb.linearDamping = 0.8f;
        rb.angularDamping = 2.5f;

        pidActive = true;
        burnTime = 25.0f;
        thrustInNewtons = 16f;
        curveAngleDegrees = 14f;
        disturbanceKickSpeed = 2.15f;
        disturbanceSidePush = 0.85f;
        maxRecoveryAngle = 34f;
        maxControlTorque = 6.5f;
        Kp = 8.5f;
        Ki = 0.8f;
        Kd = 2.6f;

        MeshCollider[] meshColliders = GetComponentsInChildren<MeshCollider>();
        foreach (MeshCollider mc in meshColliders)
        {
            mc.convex = true;
        }

        SetupTrajectoryLine();
        Setup3DExhaustPlume();
        UpdateTrailColor();
    }

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

    void UpdateLaunchRodPosition()
    {
        GameObject existingEnv = GameObject.Find("KSP_Environment");
        if (existingEnv == null) return;

        Transform rod = existingEnv.transform.Find("LaunchRodAssembly/ChromeLaunchRod");
        Transform cap = existingEnv.transform.Find("LaunchRodAssembly/RedSafetyCap");
        float padTopY = GetExactRocketBottomY();

        if (rod != null)
            rod.localPosition = new Vector3(initialPosition.x + launchRodOffsetX, padTopY + 0.42f, initialPosition.z + launchRodOffsetZ);
        if (cap != null)
            cap.localPosition = new Vector3(initialPosition.x + launchRodOffsetX, padTopY + 0.85f, initialPosition.z + launchRodOffsetZ);
    }

    // =========================================================================
    // CLEAN KSP ENVIRONMENT (Uses the Rocket's Built-In Left-Side Launch Lugs!)
    // =========================================================================
    Material CreateEnvMat(Color col)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        Material m = new Material(sh);
        m.color = col;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
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
        GameObject oldPlane = GameObject.Find("Plane");
        if (oldPlane != null && oldPlane.GetComponent<Renderer>() != null)
            oldPlane.GetComponent<Renderer>().enabled = false;
        GameObject oldCube = GameObject.Find("Cube");
        if (oldCube != null && oldCube.GetComponent<Renderer>() != null)
            oldCube.GetComponent<Renderer>().enabled = false;

        GameObject existingEnv = GameObject.Find("KSP_Environment");
        if (existingEnv != null)
        {
            if (existingEnv.transform.Find("BuiltInLugMarker") != null)
            {
                UpdateLaunchRodPosition();
                return;
            }
            if (Application.isPlaying) Destroy(existingEnv);
            else DestroyImmediate(existingEnv);
        }

        GameObject env = new GameObject("KSP_Environment");
        GameObject marker = new GameObject("BuiltInLugMarker");
        marker.transform.SetParent(env.transform, false);

        float padTopY = GetExactRocketBottomY();
        float grassY = padTopY - 0.08f;

        Material grassMat = CreateEnvMat(new Color(0.28f, 0.48f, 0.18f));
        Material concreteMat = CreateEnvMat(new Color(0.38f, 0.40f, 0.42f));
        Material darkSteelMat = CreateEnvMat(new Color(0.20f, 0.22f, 0.24f));
        Material chromeRodMat = CreateEnvMat(new Color(0.82f, 0.86f, 0.90f));
        Material clampRedMat = CreateEnvMat(new Color(0.78f, 0.15f, 0.12f));
        Material yellowLineMat = CreateEnvMat(new Color(0.85f, 0.72f, 0.15f));
        Material trunkMat = CreateEnvMat(new Color(0.32f, 0.21f, 0.12f));
        Material pineMat = CreateEnvMat(new Color(0.14f, 0.32f, 0.12f));
        Material rockMat = CreateEnvMat(new Color(0.36f, 0.37f, 0.39f)); // Fixed typo here!
        Material snowMat = CreateEnvMat(new Color(0.92f, 0.95f, 0.98f));
        Material cloudMat = CreateEnvMat(new Color(0.95f, 0.97f, 1.0f));

        // 1. Vast Green Valley Floor
        MakePart(PrimitiveType.Cube, env.transform, new Vector3(0f, grassY - 0.5f, 0f), new Vector3(1500f, 1f, 1500f), Quaternion.identity, grassMat);

        // 2. Clean Concrete Launchpad Platform (Flush on the ground, no ramp, no extra pillars)
        Vector3 padCenter = new Vector3(initialPosition.x, padTopY - 0.04f, initialPosition.z);
        MakePart(PrimitiveType.Cube, env.transform, padCenter, new Vector3(0.85f, 0.08f, 0.85f), Quaternion.identity, concreteMat);
        
        MakePart(PrimitiveType.Cylinder, env.transform, new Vector3(initialPosition.x, padTopY + 0.002f, initialPosition.z), new Vector3(0.26f, 0.004f, 0.26f), Quaternion.identity, darkSteelMat);
        MakePart(PrimitiveType.Cylinder, env.transform, new Vector3(initialPosition.x, padTopY + 0.012f, initialPosition.z), new Vector3(0.08f, 0.012f, 0.08f), Quaternion.identity, darkSteelMat);

        MakePart(PrimitiveType.Cube, env.transform, new Vector3(initialPosition.x, padTopY + 0.001f, initialPosition.z - 0.36f), new Vector3(0.72f, 0.004f, 0.02f), Quaternion.identity, yellowLineMat);
        MakePart(PrimitiveType.Cube, env.transform, new Vector3(initialPosition.x, padTopY + 0.001f, initialPosition.z + 0.36f), new Vector3(0.72f, 0.004f, 0.02f), Quaternion.identity, yellowLineMat);

        // 3. Single Chrome Launch Rod (Positioned on the LEFT side [-X] to thread right through your STL's built-in launch lugs!)
        GameObject rodGroup = new GameObject("LaunchRodAssembly");
        rodGroup.transform.SetParent(env.transform, false);

        float rodX = initialPosition.x + launchRodOffsetX;
        float rodZ = initialPosition.z + launchRodOffsetZ;

        GameObject rodObj = MakePart(PrimitiveType.Cylinder, rodGroup.transform, new Vector3(rodX, padTopY + 0.42f, rodZ), new Vector3(0.005f, 0.42f, 0.005f), Quaternion.identity, chromeRodMat);
        rodObj.name = "ChromeLaunchRod";

        GameObject capObj = MakePart(PrimitiveType.Cube, rodGroup.transform, new Vector3(rodX, padTopY + 0.85f, rodZ), new Vector3(0.014f, 0.014f, 0.014f), Quaternion.identity, clampRedMat);
        capObj.name = "RedSafetyCap";

        // 4. Scattered Pine Trees Across the Valley
        Random.State oldState = Random.state;
        Random.InitState(42);
        for (int i = 0; i < 85; i++)
        {
            float tx = Random.Range(-45f, 65f);
            float tz = Random.Range(-5f, 95f);
            if (Mathf.Abs(tx) < 3.5f && Mathf.Abs(tz) < 4.5f) continue;

            float scale = Random.Range(0.7f, 1.5f);
            Vector3 tPos = new Vector3(tx, grassY, tz);

            MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, 0.25f * scale, 0f), new Vector3(0.12f * scale, 0.25f * scale, 0.12f * scale), Quaternion.identity, trunkMat);
            MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, 0.55f * scale, 0f), new Vector3(0.65f * scale, 0.25f * scale, 0.65f * scale), Quaternion.identity, pineMat);
            MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, 0.90f * scale, 0f), new Vector3(0.48f * scale, 0.25f * scale, 0.48f * scale), Quaternion.identity, pineMat);
            MakePart(PrimitiveType.Cylinder, env.transform, tPos + new Vector3(0f, 1.22f * scale, 0f), new Vector3(0.28f * scale, 0.22f * scale, 0.28f * scale), Quaternion.identity, pineMat);
        }

        // 5. Snow-Capped Mountain Ridge
        int numPeaks = 22;
        for (int i = 0; i < numPeaks; i++)
        {
            float t = (float)i / (numPeaks - 1);
            float mx = Mathf.Lerp(-95f, 115f, t);
            float mz = Mathf.Lerp(95f, 65f, t) + Random.Range(-8f, 8f);
            float mHeight = Mathf.Lerp(14f, 42f, t) + Random.Range(-3f, 5f);
            float mWidth = Random.Range(18f, 28f);

            Quaternion mRot = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 45f), Random.Range(-10f, 10f));
            MakePart(PrimitiveType.Cube, env.transform, new Vector3(mx, grassY + mHeight * 0.4f, mz), new Vector3(mWidth, mHeight, mWidth * 0.85f), mRot, rockMat);
            MakePart(PrimitiveType.Cube, env.transform, new Vector3(mx, grassY + mHeight * 0.86f, mz - 0.5f), new Vector3(mWidth * 0.82f, mHeight * 0.22f, mWidth * 0.75f), mRot, snowMat);
        }

        // 6. High-Altitude 3D Clouds
        for (int c = 0; c < 35; c++)
        {
            float cx = Random.Range(-50f, 80f);
            float cy = Random.Range(6f, 65f);
            float cz = Random.Range(8f, 60f);
            float cScale = Random.Range(1.5f, 4.0f);
            MakePart(PrimitiveType.Sphere, env.transform, new Vector3(cx, cy, cz), new Vector3(cScale * 2.8f, cScale * 0.6f, cScale * 1.4f), Quaternion.identity, cloudMat);
        }
        Random.state = oldState;
    }

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

    void Setup3DExhaustPlume()
    {
        Transform oldParticles = transform.Find("MotorExhaustFlames");
        if (oldParticles != null) Destroy(oldParticles.gameObject);

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

    void UpdateExhaustPlumeVisuals(Vector3 currentNoseWorld)
    {
        if (flameRoot == null) return;

        bool motorBurning = isLaunched && (currentBurnTime < burnTime);
        if (flameRoot.activeSelf != motorBurning)
        {
            flameRoot.SetActive(motorBurning);
        }

        if (motorBurning)
        {
            flameRoot.transform.position = transform.position;
            flameRoot.transform.rotation = Quaternion.LookRotation(-currentNoseWorld);

            float flicker1 = Random.Range(0.16f, 0.24f);
            float flicker2 = Random.Range(0.09f, 0.14f);
            float widthJitter = Random.Range(0.032f, 0.042f);

            outerFlame.localScale = new Vector3(widthJitter, widthJitter, flicker1);
            outerFlame.localPosition = new Vector3(0f, 0f, flicker1 * 0.45f);

            innerCoreFlame.localScale = new Vector3(widthJitter * 0.6f, widthJitter * 0.6f, flicker2);
            innerCoreFlame.localPosition = new Vector3(0f, 0f, flicker2 * 0.4f);
        }
    }

    void UpdateTrailColor()
    {
        if (trajectoryLine == null) return;

        if (isStalled)
            trajectoryLine.endColor = Color.red;
        else if (pidActive)
            trajectoryLine.endColor = Color.green;
        else
            trajectoryLine.endColor = new Color(1f, 0.45f, 0f);
    }

    void FixedUpdate()
    {
        if (!Application.isPlaying || !isLaunched) return;

        currentBurnTime += Time.fixedDeltaTime;

        float curveProgress = Mathf.Clamp01((currentBurnTime - 0.3f) / 3.5f);
        float currentTargetAngle = curveProgress * curveAngleDegrees;
        Vector3 targetTrajectoryDir = Quaternion.Euler(0f, 0f, -currentTargetAngle) * Vector3.up;

        Vector3 currentNoseWorld = transform.TransformDirection(localNoseDirection);

        currentSpeed = rb.linearVelocity.magnitude;
        currentAcceleration = (rb.linearVelocity - previousVelocity).magnitude / Time.fixedDeltaTime;
        previousVelocity = rb.linearVelocity;

        currentDeviationAngle = Vector3.Angle(currentNoseWorld, targetTrajectoryDir);
        float thetaRad = currentDeviationAngle * Mathf.Deg2Rad;
        currentSolidAngleSr = 2f * Mathf.PI * (1f - Mathf.Cos(thetaRad));

        if (currentDeviationAngle > maxRecoveryAngle && currentBurnTime > 0.3f)
        {
            if (!isStalled)
            {
                isStalled = true;
                UpdateTrailColor();
            }
        }

        if (currentBurnTime < burnTime)
        {
            rb.AddForce(currentNoseWorld * thrustInNewtons, ForceMode.Force);
        }

        if (currentBurnTime < 0.25f)
        {
            rb.MoveRotation(initialRotation);
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            if (isStalled)
            {
                Vector3 stallTipTorque = Vector3.Cross(targetTrajectoryDir, currentNoseWorld);
                rb.AddTorque(stallTipTorque * 2.4f, ForceMode.Acceleration);
            }
            else if (pidActive)
            {
                Vector3 error = Vector3.Cross(currentNoseWorld, targetTrajectoryDir);

                integralError += error * Time.fixedDeltaTime;
                integralError = Vector3.ClampMagnitude(integralError, 1.0f);

                Vector3 derivativeError = -rb.angularVelocity;

                Vector3 pidTorque = (Kp * error) + (Ki * integralError) + (Kd * derivativeError);
                pidTorque = Vector3.ClampMagnitude(pidTorque, maxControlTorque);
                rb.AddTorque(pidTorque, ForceMode.Acceleration);
            }
            else
            {
                if (currentDeviationAngle > 2f)
                {
                    Vector3 driftTorque = Vector3.Cross(targetTrajectoryDir, currentNoseWorld);
                    rb.AddTorque(driftTorque * 1.6f, ForceMode.Acceleration);
                }
            }
        }

        pointRecordTimer += Time.fixedDeltaTime;
        if (pointRecordTimer >= 0.025f)
        {
            pointRecordTimer = 0f;
            pathPoints.Add(transform.position - currentNoseWorld * 0.15f);
            trajectoryLine.positionCount = pathPoints.Count;
            trajectoryLine.SetPositions(pathPoints.ToArray());
        }
    }

    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        Vector3 currentNoseWorld = transform.TransformDirection(localNoseDirection);
        UpdateExhaustPlumeVisuals(currentNoseWorld);
    }

    void Update()
    {
        if (!Application.isPlaying || Keyboard.current == null) return;

        if ((Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) && !isLaunched)
        {
            isLaunched = true;
            rb.isKinematic = false;
            previousVelocity = Vector3.zero;
            pathPoints.Clear();
            UpdateTrailColor();
        }

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            ResetRocket();
        }

        if (Keyboard.current.dKey.wasPressedThisFrame && isLaunched)
        {
            float timeSinceLastGust = Time.time - lastDisturbTime;
            if (timeSinceLastGust > 2.0f)
            {
                lastDisturbDir = -lastDisturbDir;
            }
            lastDisturbTime = Time.time;

            rb.angularVelocity += new Vector3(0f, 0f, lastDisturbDir * disturbanceKickSpeed);
            rb.AddForce(new Vector3(-lastDisturbDir * disturbanceSidePush, 0f, 0f), ForceMode.VelocityChange);
        }

        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            pidActive = !pidActive;
            integralError = Vector3.zero;
            UpdateTrailColor();
        }
    }

    void ResetRocket()
    {
        isLaunched = false;
        isStalled = false;
        currentBurnTime = 0f;
        integralError = Vector3.zero;
        pidActive = true;
        UpdateTrailColor();

        currentSpeed = 0f;
        currentAcceleration = 0f;
        currentDeviationAngle = 0f;
        currentSolidAngleSr = 0f;
        previousVelocity = Vector3.zero;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        transform.position = initialPosition;
        transform.rotation = initialRotation;

        pathPoints.Clear();
        if (trajectoryLine != null) trajectoryLine.positionCount = 0;
        if (flameRoot != null) flameRoot.SetActive(false);
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

        string statusText;
        if (isStalled)
            statusText = "<color=#FF3333>STALLED (>34° - Unrecoverable!)</color>";
        else if (pidActive)
            statusText = "<color=#00FF00>ON (Active Stabilization)</color>";
        else
            statusText = "<color=#FF9900>OFF (Unguided Flight)</color>";

        string degColor = isStalled ? "#FF3333" : (currentDeviationAngle > 18f ? "#FFFF00" : "#00FFFF");

        string info = $"ROCKET AVIONICS & TELEMETRY\n" +
                      $"PID Loop [S]: {statusText}\n" +
                      $"Velocity: <color=#00FFFF>{currentSpeed:F1} m/s</color>   |   Accel: <color=#00FFFF>{currentAcceleration:F1} m/s²</color>\n" +
                      $"Trajectory Deviation: <color={degColor}>{currentDeviationAngle:F1}°</color>  (Solid Angle: <color={degColor}>{currentSolidAngleSr:F3} sr</color>)\n" +
                      $"Controls: [Space] Launch  |  [D] Wind Gust  |  [R] Reset";

        GUI.Box(new Rect(25, 12, 380, 92), info, style);
    }
}