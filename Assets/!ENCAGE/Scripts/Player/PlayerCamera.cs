using FishNet.Object;
using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerCamera : NetworkBehaviour
{
    [Header("Main")]
    [SerializeField] private GameObject cameraPrefab;
    [SerializeField] private Transform visualCameraTarget;
    [SerializeField] private float rotationSmooth = 20f;

    [Header("Sensitivity")]
    [SerializeField] private float mouseSensitivity = 100f;
    [SerializeField] private float pitchLimit = 85f;

    [Header("Sway Settings")]
    [SerializeField] private float swayLerpSpeed;
    [SerializeField] private float maxSwayY = 0.12f;
    [SerializeField] private float swayXForce = 0.08f;
    [SerializeField] private float swaySmoothTime = 0.08f;

    [Header("Tilt Settings")]
    [SerializeField] private float maxTiltAngle = 10f;
    [SerializeField] private float tiltPunchMultiplier = 8f;

    [Header("Punch / Spring Settings")]
    [SerializeField] private float punchDamping = 5f;
    [SerializeField] private float punchSpring = 80f;
    [SerializeField] private float punchIntensity = 1.5f;
    [SerializeField] private float punchClamp = 80f;

    [Header("Bob / Walk Settings")]
    [SerializeField] private float walkBobFrequency = 10f;
    [SerializeField] private float walkBobVertical = 0.06f;
    [SerializeField] private float walkBobHorizontal = 0.04f;
    [SerializeField] private float runBobMultiplier = 1.5f;
    [SerializeField] private float bobLerp = 8f;

    [Header("Breath")]
    [SerializeField] private float breathVertical = 0.02f;
    [SerializeField] private float breathAngle = 0.15f;
    [SerializeField] private float breathBaseFrequency = 1.6f;
    [SerializeField] private float breathMaxFrequency = 3.2f;
    [SerializeField] private float breathIntensityMultiplier = 1.0f;
    [SerializeField] private float exhaustionSmoothSpeed = 6f;

    [Header("Exhaustion Post-Processing")]
    [SerializeField] private float maxSaturationDecrease = -45f;
    [SerializeField] private float maxContrastIncrease = 35f;
    [SerializeField] private float postProcessLerpSpeed = 8f;

    // === НОВЫЙ ЗАГОЛОВОК И ПАРАМЕТРЫ ДЛЯ ВИНЬЕТКИ ===
    [Header("Damage Flash (Vignette)")]
    [SerializeField] private float vignetteFlashMaxIntensity = 0.6f;     // Максимальная интенсивность виньетки при дамаге
    [SerializeField] private float vignetteFlashDecaySpeed = 8f;        // Скорость затухания (≈0.3 секунды до почти нуля)
    [SerializeField] private Color vignetteFlashTargetColor = Color.red; // Цвет виньетки во время флеша

    private ColorAdjustments colorAdjustments;
    private float initialSaturation;
    private float initialContrast;
    private float currentSaturationTarget;
    private float currentContrastTarget;

    // === НОВЫЕ ПЕРЕМЕННЫЕ ДЛЯ ВИНЬЕТКИ ===
    private Vignette vignette;
    private float initialVignetteIntensity;
    private Color initialVignetteColor;
    private float vignetteFlashStrength;

    private float _smoothedExhaustion;
    private float breathCycleTime;

    private PlayerInput pInput;
    private PlayerUI pUI;
    private PlayerRagdoll pRagdoll;
    private PlayerMovement pMovement;

    public Transform cameraHolder { get; private set; }
    public Transform cameraOffset { get; private set; }
    public Transform viewCamera { get; private set; }

    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

    private float vmTime;

    private Vector3 springAngle;
    private Vector3 springVelocity;

    private float swayOffsetY;
    private float swayVelocity;
    private float lerpedSwayX;
    private float lerpedSwayY;
    private Vector2 lastLook;

    private Vector3 appliedPosOffset;
    private Vector3 appliedAngOffset;

    private Vector3 initialCameraOffsetEuler;
    private Vector3 initialViewCameraLocalPos;
    private bool _justStoodUp = false;

    private Volume ppVolume;

    public void OnStandup() { _justStoodUp = true; }

    public void AddViewPunch(Vector3 punch)
    {
        springVelocity += punch * punchIntensity;
    }

    private void UpdateExhaustion(float dt)
    {
        float staminaPercent = pMovement.CurrentStamina / pMovement.MaxStamina;
        float target = (1f - staminaPercent) * 0.5f;

        float alpha = 1f - Mathf.Exp(-exhaustionSmoothSpeed * dt);
        _smoothedExhaustion = Mathf.Lerp(_smoothedExhaustion, target, alpha);
    }

    private void ApplyExhaustionPostProcessing(float dt)
    {
        if (colorAdjustments == null) return;

        float norm = _smoothedExhaustion / 0.5f;
        norm = Mathf.Clamp01(norm);

        float targetSat = initialSaturation + maxSaturationDecrease * norm;
        float targetCon = initialContrast + maxContrastIncrease * norm;

        float alpha = 1f - Mathf.Exp(-postProcessLerpSpeed * dt);
        currentSaturationTarget = Mathf.Lerp(currentSaturationTarget, targetSat, alpha);
        currentContrastTarget = Mathf.Lerp(currentContrastTarget, targetCon, alpha);

        colorAdjustments.saturation.value = currentSaturationTarget;
        colorAdjustments.contrast.value = currentContrastTarget;
    }

    // ===================================================================
    // НОВЫЙ TakeDamageFlash — теперь работает через Vignette с красивым стакингом
    // ===================================================================
    public void TakeDamageFlash(float strength01)
    {
        if (vignette == null) return;

        // Стакаем силу флеша (каждый новый урон добавляет эффект, не ресетит)
        // Даже при постоянном дамаге эффект усиливается и держится дольше
        vignetteFlashStrength = Mathf.Clamp01(vignetteFlashStrength + strength01);
    }

    // ===================================================================
    // Новый метод — применяется каждый LateUpdate
    // ===================================================================
    private void ApplyDamageFlash(float dt)
    {
        if (vignette == null) return;

        // Плавное экспоненциальное затухание (очень красиво и естественно)
        float decayAlpha = 1f - Mathf.Exp(-vignetteFlashDecaySpeed * dt);
        vignetteFlashStrength = Mathf.Lerp(vignetteFlashStrength, 0f, decayAlpha);

        // Нормализованная сила эффекта (0..1)
        float norm = vignetteFlashStrength;

        // Intensity: плавно от дефолтного значения → 0.6
        float targetIntensity = Mathf.Lerp(initialVignetteIntensity, vignetteFlashMaxIntensity, norm);

        // Color: плавно от дефолтного чёрного → красный
        Color targetColor = Color.Lerp(initialVignetteColor, vignetteFlashTargetColor, norm);

        vignette.intensity.value = targetIntensity;
        vignette.color.value = targetColor;
    }

    public override void OnStartClient()
    {
        if (!IsOwner) return;

        pInput = GetComponent<PlayerInput>();
        pUI = GetComponent<PlayerUI>();
        pRagdoll = GetComponent<PlayerRagdoll>();
        pMovement = GetComponent<PlayerMovement>();

        cameraHolder = Instantiate(cameraPrefab).transform;
        cameraOffset = cameraHolder.Find("CameraOffset");
        viewCamera = cameraOffset.Find("Camera");

        Transform audioListener = FindFirstObjectByType<AudioListener>().transform;

        audioListener.parent = cameraHolder;
        audioListener.localPosition = Vector3.zero;

        pUI.SettinUpCanvas(viewCamera);

        initialCameraOffsetEuler = cameraOffset.localEulerAngles;
        initialViewCameraLocalPos = viewCamera.localPosition;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Yaw = transform.eulerAngles.y;
        lastLook = pInput.Look;

        ppVolume = GameObject.FindWithTag("Post-Processing").GetComponent<Volume>();

        if (ppVolume != null && ppVolume.profile != null)
        {
            if (ppVolume.profile.TryGet(out colorAdjustments))
            {
                initialSaturation = colorAdjustments.saturation.value;
                initialContrast = colorAdjustments.contrast.value;

                currentSaturationTarget = initialSaturation;
                currentContrastTarget = initialContrast;
            }

            // === ИНИЦИАЛИЗАЦИЯ ВИНЬЕТКИ ===
            if (ppVolume.profile.TryGet(out vignette))
            {
                initialVignetteIntensity = vignette.intensity.value;
                initialVignetteColor = vignette.color.value;
            }
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        float dt = Time.deltaTime;

        if (pRagdoll.IsRagdolled)
        {
            cameraHolder.rotation = Quaternion.RotateTowards(
                cameraHolder.rotation,
                visualCameraTarget.rotation,
                360f * dt
            );
            return;
        }

        if (_justStoodUp)
        {
            _justStoodUp = false;
            cameraHolder.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            return;
        }

        Vector2 look = pInput.Look;

        Yaw += look.x * mouseSensitivity;
        Pitch -= look.y * mouseSensitivity;
        Pitch = Mathf.Clamp(Pitch, -pitchLimit, pitchLimit);

        Quaternion targetRotation = Quaternion.Euler(Pitch, Yaw, 0f);

        float rotationT = 1f - Mathf.Exp(-rotationSmooth * dt);
        cameraHolder.rotation = Quaternion.Slerp(
            cameraHolder.rotation,
            targetRotation,
            rotationT
        );
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        float dt = Time.deltaTime;

        cameraHolder.position = visualCameraTarget.position;

        UpdateExhaustion(dt);
        ApplyExhaustionPostProcessing(dt);
        ApplyLookSway(dt);
        UpdateSway(dt);
        UpdateVMTime(dt);
        ApplyTilt(dt);
        IntegrateSpring(dt);
        ApplyPunch();
        ApplyBobAndBreath(dt);

        // === НОВЫЙ ВЫЗОВ ДЛЯ ДАМАГ-ФЛЕША ===
        ApplyDamageFlash(dt);
    }

    private void ApplyLookSway(float dt)
    {
        Vector2 look = pInput.Look;
        Vector2 delta = look - lastLook;
        lastLook = look;

        float swayAlpha = 1f - Mathf.Exp(-swayLerpSpeed * dt);
        lerpedSwayX = Mathf.Lerp(lerpedSwayX, delta.x * 100f, swayAlpha);
        lerpedSwayY = Mathf.Lerp(lerpedSwayY, delta.y * 100f, swayAlpha);

        AddViewPunch(new Vector3(-lerpedSwayY, -lerpedSwayX, 0f) * 0.003f);
    }

    private void ApplyPunch()
    {
        cameraOffset.localRotation =
            Quaternion.Euler(initialCameraOffsetEuler) *
            Quaternion.Euler(springAngle);
    }

    private void ApplyBobAndBreath(float dt)
    {
        float speed = pMovement.GetFlatVelocity().magnitude;
        float norm = _smoothedExhaustion / 0.5f;

        float frequency = Mathf.Lerp(breathBaseFrequency, breathMaxFrequency, norm * 0.7f);
        breathCycleTime += dt * frequency;

        float cycle = Mathf.Sin(breathCycleTime);

        float shaped = cycle > 0f
            ? Mathf.Pow(cycle, 0.6f)
            : -Mathf.Pow(-cycle, 1.4f);

        float amplitude = breathIntensityMultiplier * (0.4f + norm * 1.2f);

        float verticalBreath = shaped * breathVertical * amplitude;
        float horizontalBreath = shaped * breathAngle * amplitude * 0.3f;

        float verticalBob = Mathf.Sin(vmTime) * walkBobVertical;
        float horizontalBob = Mathf.Sin(vmTime * 0.5f) * walkBobHorizontal;

        Vector3 targetPos = new Vector3(
            horizontalBob,
            verticalBob + verticalBreath + swayOffsetY,
            0f
        );

        float bobAlpha = 1f - Mathf.Exp(-bobLerp * dt);
        appliedPosOffset = Vector3.Lerp(appliedPosOffset, targetPos, bobAlpha);

        viewCamera.localPosition = initialViewCameraLocalPos + appliedPosOffset;

        if (norm > 0.9f)
        {
            float panic = (norm - 0.9f) * 10f;

            if (Random.value < dt * 6f * panic)
            {
                AddViewPunch(new Vector3(
                    Random.Range(-0.2f, 0.2f),
                    Random.Range(-0.2f, 0.2f),
                    0f
                ) * 0.6f);
            }
        }
    }

    private void UpdateSway(float dt)
    {
        float mouseX = pInput.Look.x;
        float targetSwayY = -Mathf.Abs(mouseX) * swayXForce;
        targetSwayY = Mathf.Clamp(targetSwayY, -maxSwayY, 0f);

        swayOffsetY = Mathf.SmoothDamp(swayOffsetY, targetSwayY, ref swayVelocity, swaySmoothTime);
    }

    private void UpdateVMTime(float dt)
    {
        float speed = pMovement.GetFlatVelocity().magnitude;

        if (speed > 0.1f && pMovement.IsGrounded)
        {
            float mult = pMovement.IsSprinting ? runBobMultiplier : 1f;
            vmTime += dt * walkBobFrequency * mult;
        }
    }


    private void IntegrateSpring(float dt)
    {
        if (dt <= 0f) return;

        Vector3 acceleration = -springAngle * punchSpring - springVelocity * punchDamping;
        springVelocity += acceleration * dt;
        springAngle += springVelocity * dt;

        springAngle.x = Mathf.Clamp(springAngle.x, -punchClamp, punchClamp);
        springAngle.y = Mathf.Clamp(springAngle.y, -punchClamp, punchClamp);
        springAngle.z = Mathf.Clamp(springAngle.z, -punchClamp, punchClamp);
    }

    private void ApplyTilt(float dt)
    {
        Vector3 localVel = transform.InverseTransformDirection(pMovement.GetFlatVelocity());
        float tilt = -localVel.x / pMovement.CurrentMaxSpeed * maxTiltAngle;

        AddViewPunch(new Vector3(0f, 0f, tilt * tiltPunchMultiplier * dt));
    }
}