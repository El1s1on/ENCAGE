using FishNet;
using FishNet.Component.Transforming.Beta;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed;
    [SerializeField] private float sprintSpeed;
    [Header("Gravity & Jump")]
    [SerializeField] private float gravity = -30f;
    [SerializeField] private float groundedGravity = -2f;
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float slopeStickForce = -4f;
    [SerializeField] private float maxAirSpeedMultiplier = 1.1f;
    [Header("Ground Check")]
    [SerializeField] private float groundSphereRadius = 0.3f;
    [SerializeField] private float fallSphereRadius = 0.05f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float groundCheckDistance = 0.05f;
    [Header("Ragdoll Stand-up")]
    [SerializeField] private LayerMask groundMask;
    [Header("Acceleration")]
    [SerializeField] private float groundAcceleration = 55f;
    [SerializeField] private float groundDeceleration = 70f;
    [SerializeField] private float airAcceleration = 20f;
    [SerializeField] private float airDeceleration = 5f;
    private CharacterController characterController;
    private PlayerInput pInput;
    private PlayerAnimations pAnimations;
    private PlayerRagdoll pRagdoll;
    private Vector3 inputDir;
    private Vector3 flatVelocity;
    private Vector3 velocity;
    private Vector3 horizontalMove;
    private uint coyoteTicks;
    private uint lastGroundTick;
    private int physicsMask;
    private RaycastHit cachedHitInfo;
    private bool _wasRagdolled;
    private bool _clientRagdollState;
    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        characterController = GetComponent<CharacterController>();
        physicsMask = ~(1 << gameObject.layer);
        pAnimations = GetComponent<PlayerAnimations>();
        pInput = GetComponent<PlayerInput>();
        pRagdoll = GetComponent<PlayerRagdoll>();
        float tickDelta = (float)TimeManager.TickDelta;
        coyoteTicks = (uint)Mathf.Round(coyoteTime / tickDelta);
        _wasRagdolled = pRagdoll.IsRagdolled;
        _clientRagdollState = pRagdoll.IsRagdolled;
        TimeManager.OnTick += OnTick;
        TimeManager.OnPostTick += OnPostTick;
    }
    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        TimeManager.OnTick -= OnTick;
        TimeManager.OnPostTick -= OnPostTick;
    }
    private void OnTick()
    {
        PlayerInputData input = default;
        if (IsOwner)
            input = pInput.ConsumeInput();
        ReplicationData data = new ReplicationData(input);
        Replicate(data);
    }
    private void OnPostTick()
    {
        CreateReconcile();
    }
    [Replicate]
    private void Replicate(ReplicationData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        // Для тестов, если нужно вызывать с клиента (можно удалить в продакшене)
        if (data.input.Debug && state.ContainsTicked() && IsServerStarted)
        {
            GetComponent<PlayerHealth>().TakeDamage(3f);

            //if (pRagdoll.IsRagdolled)
            //    pRagdoll.DeactivateRagdoll();
            //else
            //    pRagdoll.ActivateRagdoll();
        }

        if (pRagdoll.IsRagdolled)
        {
            velocity = Vector3.zero;
            flatVelocity = Vector3.zero;
            return;
        }
        float dt = (float)TimeManager.TickDelta;
        if (!IsServerStarted && !IsOwner)
        {
            characterController.Move(new Vector3(0f, -1f, 0f) * dt);
            return;
        }
        uint currentTick = data.GetTick();
        PlayerInputData input = data.input;
        transform.rotation = Quaternion.Euler(0f, input.Yaw, 0f);
        ReadInput(input);
        GroundCheck(currentTick);
        ApplyGravity(dt, currentTick);
        HandleMovement(input, dt, currentTick);
        HandleJump(input, currentTick);
        MoveCharacter(dt);
        if (IsServerStarted)
        {
            Vector3 localVelocity = transform.InverseTransformDirection(flatVelocity);
            float maxCurrentSpeed = input.Sprint ? sprintSpeed : walkSpeed;
            float dirX = 0f;
            float dirZ = 0f;
            if (flatVelocity.sqrMagnitude > 0.1f && maxCurrentSpeed > 0f)
            {
                dirX = localVelocity.x / maxCurrentSpeed;
                dirZ = localVelocity.z / maxCurrentSpeed;
            }
            bool isGrounded = currentTick - lastGroundTick <= coyoteTicks;
            pAnimations.SetMovementState(dirX, dirZ, isGrounded, input.Sprint, dt);
        }
    }
    public override void CreateReconcile()
    {
        bool performStandup = IsServerStarted && pRagdoll.HasPendingStandup;
        Vector3 standPos = pRagdoll.PendingStandupPosition;
        Quaternion standRot = pRagdoll.PendingStandupRotation;

        if (performStandup)
        {
            characterController.enabled = false;
            transform.position = standPos;
            transform.rotation = standRot;
            characterController.enabled = true;

            velocity = Vector3.zero;
            flatVelocity = Vector3.zero;
            horizontalMove = Vector3.zero;
            pRagdoll.ResetBonesToRoot();
            characterController.Move(Vector3.zero);

            // ← СНАП В ПОЛ (самое важное)
            characterController.Move(Vector3.down * 0.05f);

            pRagdoll.SetIsRagdolled(false);

            // Force grounded сразу
            lastGroundTick = base.TimeManager.Tick;
        }

        Reconcile(new ReconcileData(
            transform.position, velocity, flatVelocity, transform.rotation,
            pRagdoll.IsRagdolled, lastGroundTick,
            standPos, standRot,
            performStandup
        ));

        // Cleaning
        pRagdoll.PendingStandupPosition = Vector3.zero;
        pRagdoll.PendingStandupRotation = Quaternion.identity;
    }

    [Reconcile]
    private void Reconcile(ReconcileData data, Channel channel = Channel.Unreliable)
    {
        bool isStandupReconcile = data.PerformStandup;

        characterController.enabled = false;

        if (isStandupReconcile)
        {
            pRagdoll.EnableTickSmoother(false);

            // === ТЕЛЕПОРТ ПОКА ЕЩЁ В РЕГДОЛЛЕ ===
            transform.position = data.standupPos;
            transform.rotation = data.standupRot;
            pRagdoll.ResetBonesToRoot();

            // ← КЛЮЧЕВОЕ ДЛЯ НАБЛЮДАТЕЛЕЙ
            pRagdoll.SnapRagdollBonesToCurrent();

            pRagdoll.EnableTickSmoother(true);
        }
        else
        {
            transform.position = data.pos;
            transform.rotation = data.rot;
        }

        // Теперь меняем состояние (камера, аниматор и т.д.)
        if (pRagdoll.IsRagdolled != data.IsRagdolled)
            pRagdoll.SetIsRagdolled(data.IsRagdolled);

        characterController.enabled = !data.IsRagdolled;

        velocity = data.vel;
        flatVelocity = data.flatVel;
        lastGroundTick = data.groundTick;

        if (isStandupReconcile)
        {
            velocity = Vector3.zero;
            flatVelocity = Vector3.zero;
            horizontalMove = Vector3.zero;

            characterController.Move(Vector3.down * 0.05f);
            characterController.Move(Vector3.zero);
        }
    }
    public Vector3 GetVelocity() => velocity;
    public void ResetVelocity()
    {
        velocity = Vector3.zero;
        flatVelocity = Vector3.zero;
    }
    public void SetControllerEnabled(bool enabled)
    {
        if (characterController != null)
            characterController.enabled = enabled;
    }
    private void ReadInput(PlayerInputData inputs)
    {
        Vector3 raw = transform.forward * inputs.Move.y + transform.right * inputs.Move.x;
        inputDir = raw.normalized;
    }
    private void HandleJump(PlayerInputData inputs, uint currentTick)
    {
        bool grounded = currentTick - lastGroundTick <= coyoteTicks;
        if (!inputs.Jump) return;
        if (!grounded) return;
        velocity.y = jumpHeight;
    }
    private void HandleMovement(PlayerInputData inputs, float dt, uint currentTick)
    {
        float speed = inputs.Sprint ? sprintSpeed : walkSpeed;
        bool grounded = currentTick - lastGroundTick <= coyoteTicks;
        if (grounded)
            HandleGroundMove(speed, dt);
        else
            HandleAirMove(speed, dt);
    }
    private void HandleAirMove(float speed, float dt)
    {
        float maxAirSpeed = speed * maxAirSpeedMultiplier;
        Vector3 targetVelocity = inputDir * maxAirSpeed;
        float accel = inputDir.sqrMagnitude > 0.01f ? airAcceleration : airDeceleration;
        Vector3 delta = targetVelocity - flatVelocity;
        float dist = delta.magnitude;
        if (dist > 0f)
        {
            Vector3 direction = delta / dist;
            float step = accel * dt;
            if (step > dist) step = dist;
            flatVelocity += direction * step;
        }
        float mag = flatVelocity.magnitude;
        if (mag > maxAirSpeed)
            flatVelocity = (flatVelocity / mag) * maxAirSpeed;
        velocity.x = flatVelocity.x;
        velocity.z = flatVelocity.z;
        horizontalMove = flatVelocity;
    }
    private void HandleGroundMove(float speed, float dt)
    {
        Vector3 targetVelocity = inputDir * speed;
        float accel = inputDir.sqrMagnitude > 0.01f ? groundAcceleration : groundDeceleration;
        Vector3 delta = targetVelocity - flatVelocity;
        float dist = delta.magnitude;
        if (dist > 0f)
        {
            Vector3 direction = delta / dist;
            float step = accel * dt;
            if (step > dist) step = dist;
            flatVelocity += direction * step;
        }
        horizontalMove = flatVelocity;
        if (velocity.y < 0)
            velocity.y = slopeStickForce;
    }
    private void MoveCharacter(float dt)
    {
        Vector3 move = horizontalMove + Vector3.up * velocity.y;
        CollisionFlags flags = characterController.Move(move * dt);
        if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0)
            velocity.y = groundedGravity;
        if ((flags & CollisionFlags.Below) != 0 && velocity.y < 0)
            velocity.y = groundedGravity;
    }
    private void GroundCheck(uint currentTick)
    {
        float radius = (velocity.y < 0f) ? fallSphereRadius : groundSphereRadius;
        Vector3 origin = transform.position + Vector3.up * radius;
        Vector3 direction = Vector3.down;
        bool hit = Physics.SphereCast(
        origin,
        radius,
        direction,
        out cachedHitInfo,
        groundCheckDistance,
        physicsMask,
        QueryTriggerInteraction.Ignore
        );
        if (hit)
            lastGroundTick = currentTick;
    }
    private void ApplyGravity(float dt, uint currentTick)
    {
        bool grounded = currentTick - lastGroundTick <= coyoteTicks;
        if (grounded && velocity.y < groundedGravity)
            velocity.y = groundedGravity;
        else
            velocity.y += gravity * dt;
    }
}