using FishNet.Object;
using Ilumisoft.Rendering;
using PrimeTween;
using UnityEngine;
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
    private float sensitivityMultiplier = 1f;

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

    [Header("Stun")]
    [SerializeField] private float stunSensitivityRecoveryTime = 1f;
    [SerializeField] private Vector3 stunViewPunch;
    private BlurVolumeComponent blur;

    private float _smoothedExhaustion;
    private float breathCycleTime;

    private PlayerContext ctx;
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

    public void StunEffect()
    {
        float startValue = blur.intensity.value;

        AddViewPunch(stunViewPunch);

        Tween.Custom(startValue, 1f, 0.2f, value => blur.intensity.value = value)
            .Chain(Tween.Delay(0.1f))
            .Chain(Tween.Custom(1f, 0f, 0.7f, value => blur.intensity.value = value));

        Tween.Custom(sensitivityMultiplier, 0f, 0.2f, value => sensitivityMultiplier = value)
            .Chain(Tween.Delay(0.3f))
            .Chain(Tween.Custom(0f, 1f, stunSensitivityRecoveryTime, value => sensitivityMultiplier = value));
    }

    public override void OnStartClient()
    {
        if (!IsOwner) return;

        ctx = PlayerContext.Get(NetworkObject);
        pInput = ctx.Input;
        pUI = ctx.UI;
        pRagdoll = ctx.Ragdoll;
        pMovement = ctx.Movement;

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
            if (ppVolume.profile.TryGet(out blur))
            {
                blur.intensity.value = 0;
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

        float currentSensitivity = mouseSensitivity * sensitivityMultiplier;
        Yaw += look.x * currentSensitivity;
        Pitch -= look.y * currentSensitivity;
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
        ApplyLookSway(dt);
        UpdateSway(dt);
        UpdateVMTime(dt);
        ApplyTilt(dt);
        IntegrateSpring(dt);
        ApplyPunch();
        ApplyBobAndBreath(dt);
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