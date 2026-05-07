using FishNet.Example.ColliderRollbacks;
using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Object;
using FishNet.Transporting;
using System;
using UnityEngine.InputSystem;
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

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 150f;
    [SerializeField] private float staminaRegenPerSecond = 25f;
    [SerializeField] private float sprintStaminaDrainPerSecond = 35f;
    [SerializeField] private float jumpStaminaCost = 15f;
    [SerializeField] private float depletedRegenDelaySeconds = 1.5f;
    private float _currentStamina;
    private float _regenDelayRemaining;

    [Header("Stun Settings")]    
    [SerializeField] private float retreatSpeed;
    [SerializeField] private float stunTime;
    [SerializeField] private float retreatTime;
    private Vector3 retreatDirection;
    private uint stunTicks;
    private uint retreatTicks;
    private uint _stunStartTick = uint.MaxValue;
    private uint _stunEndTick = uint.MaxValue;
    private uint _retreatStartTick = uint.MaxValue;
    private uint _retreatEndTick = uint.MaxValue;
    private Vector3 _stunnerPosition;

    private CharacterController characterController;
    private PlayerContext ctx;
    private PlayerInput pInput;
    private PlayerAnimations pAnimations;
    private PlayerCamera pCamera;
    private PlayerRagdoll pRagdoll;

    private Vector3 inputDir;
    private Vector3 flatVelocity;
    private Vector3 velocity;
    private Vector3 horizontalMove;
    private uint coyoteTicks;
    private uint lastGroundTick;
    private float landImpulse;

    public float CurrentMaxSpeed { get; private set; }
    public bool IsSprinting { get; private set; }

    public Action onReconcile;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        ctx = PlayerContext.Get(NetworkObject);
        characterController = ctx.CharacterController;
        pAnimations = ctx.Animations;
        pInput = ctx.Input;
        pRagdoll = ctx.Ragdoll;
        pCamera = ctx.Camera;

        float tickDelta = (float)TimeManager.TickDelta;
        coyoteTicks = (uint)Mathf.Round(coyoteTime / tickDelta);
        stunTicks = (uint)Mathf.Round(stunTime / tickDelta);
        retreatTicks = (uint)Mathf.Round(retreatTime / tickDelta);
        _currentStamina = maxStamina;

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
        if (data.input.Debug && state.ContainsTicked() && IsServerStarted)
        {
            //if (pRagdoll.IsRagdolled)
            //    pRagdoll.DeactivateRagdoll();
            //else
            //    pRagdoll.ActivateRagdoll(flatVelocity);

            if(!IsStunned(TimeManager.Tick))
                ApplyStun(transform.position + transform.forward);
        }

        if (pRagdoll.IsRagdolled || !characterController.enabled)
        {
            velocity = Vector3.zero;
            flatVelocity = Vector3.zero;
            return;
        }

        uint currentTick = data.GetTick();
        float dt = (float)TimeManager.TickDelta;

        PlayerInputData input = data.input;

        if (IsOwner || IsServerStarted)
        {
            if (_regenDelayRemaining > 0f)
            {
                _regenDelayRemaining -= dt;
            }
            else
            {
                _currentStamina = Mathf.Min(_currentStamina + staminaRegenPerSecond * dt, maxStamina);
            }
        }

        if (!IsServerStarted && !IsOwner)
        {
            return;
        }

        transform.rotation = Quaternion.Euler(0f, input.Yaw, 0f);
        pAnimations.SetPitch(input.Pitch);

        ReadInput(input);
        GroundCheck(state.ContainsReplayed(), currentTick);

        IsGrounded = currentTick - lastGroundTick <= coyoteTicks;

        ApplyGravity(dt, currentTick);

        if (IsStunned(currentTick))
        {
            // Находимся в фазе отступления
            if (currentTick >= _retreatStartTick && currentTick <= _retreatEndTick)
            {
                flatVelocity = Vector3.zero;
                horizontalMove = retreatDirection * retreatSpeed;
                MoveCharacter(dt);
            }
            // Иначе просто стоим на месте (активная фаза стана после отступления)

            return; // Блокируем всё остальное движение
        }

        HandleMovement(input, dt, currentTick);
        HandleJump(input, state.ContainsReplayed(), currentTick);
        MoveCharacter(dt);

        CurrentMaxSpeed = input.Sprint ? sprintSpeed : walkSpeed;

        if (IsServerStarted)
        {
            Vector3 localVelocity = transform.InverseTransformDirection(flatVelocity);
            float dirX = 0f;
            float dirZ = 0f;
            if (flatVelocity.sqrMagnitude > 0.1f && CurrentMaxSpeed > 0f)
            {
                dirX = localVelocity.x / CurrentMaxSpeed;
                dirZ = localVelocity.z / CurrentMaxSpeed;
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
            characterController.transform.position = transform.position;
            velocity = Vector3.zero;
            flatVelocity = Vector3.zero;
            horizontalMove = Vector3.zero;
            pRagdoll.PerformInstantStandupSnap();
            //characterController.Move(Vector3.down * 0.05f);
            //characterController.Move(Vector3.zero);

            if (IsServerStarted)
                pRagdoll.SetIsRagdolled(false);

            lastGroundTick = base.TimeManager.Tick;
            pRagdoll.PendingStandupPosition = Vector3.zero;
            pRagdoll.PendingStandupRotation = Quaternion.identity;
            pRagdoll.HasPendingStandup = false;
        }

        uint start = _stunStartTick;
        uint end = _stunEndTick;
        bool stunned = (TimeManager.Tick >= start && TimeManager.Tick <= end);

        Reconcile(new ReconcileData(
            transform.position, velocity, flatVelocity, transform.rotation, pAnimations.clampedPitch,
            pRagdoll.IsRagdolled, lastGroundTick,
            _stunnerPosition, // позиция оглушившего
            stunned, start, end,     // флаг "в стане сейчас"
            retreatDirection, // текущее направление отступления (даже если стан кончился, оно не повлияет)
            standPos, standRot, performStandup,
            _currentStamina,
            _regenDelayRemaining
        ));
    }

    [Reconcile]
    private void Reconcile(ReconcileData data, Channel channel = Channel.Unreliable)
    {
        bool isStandupReconcile = data.PerformStandup;
        characterController.enabled = false;

        if (isStandupReconcile)
        {
            transform.position = data.standupPos;
            transform.rotation = data.standupRot;
            pRagdoll.PerformInstantStandupSnap();

            if (IsOwner && pCamera != null)
                pCamera.OnStandup();

            GroundCheck(false, data.GetTick());
        }
        else
        {
            transform.position = data.pos;
            transform.rotation = data.rot;
            pAnimations.SetPitch(data.pitch);
        }

        characterController.enabled = !data.IsRagdolled;

        velocity = data.vel;
        flatVelocity = data.flatVel;
        lastGroundTick = data.groundTick;
        IsGrounded = (data.GetTick() - lastGroundTick <= coyoteTicks);

        if (data.stunStartTick != uint.MaxValue)
        {
            _stunStartTick = TimeManager.TickToLocalTick(data.stunStartTick);
            _stunEndTick = TimeManager.TickToLocalTick(data.stunEndTick);
            _stunnerPosition = data.stunnerPos;
            retreatDirection = (transform.position - _stunnerPosition).normalized;
            _retreatStartTick = _stunStartTick;
            _retreatEndTick = _stunStartTick + retreatTicks;
        }
        else
        {
            _stunStartTick = uint.MaxValue;
            _stunEndTick = uint.MaxValue;
        }

        _currentStamina = data.stamina;
        _regenDelayRemaining = data.regenDelay;

        if (isStandupReconcile)
        {
            velocity = Vector3.zero;
            flatVelocity = Vector3.zero;
            horizontalMove = Vector3.zero;
            characterController.Move(Vector3.down * 0.05f);
            characterController.Move(Vector3.zero);
        }

        onReconcile?.Invoke();
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

    private void HandleJump(PlayerInputData inputs, bool isReplayed, uint currentTick)
    {
        bool grounded = currentTick - lastGroundTick <= coyoteTicks;
        if (!inputs.Jump) return;
        if (!grounded) return;
        if (_currentStamina < 1) return;
        ConsumeStamina(jumpStaminaCost, 0.6f);

        pAnimations.TriggerJump();

        if (IsOwner && !isReplayed)
        {
            pCamera.AddViewPunch(Vector3.left * 75f);
        }

        velocity.y = jumpHeight;
    }

    private void HandleMovement(PlayerInputData inputs, float dt, uint currentTick)
    {
        float speed = walkSpeed;
        IsSprinting = false;

        if (inputs.Sprint && _currentStamina >= sprintStaminaDrainPerSecond * dt && flatVelocity.sqrMagnitude > 0.1f)
        {
            ConsumeStamina(sprintStaminaDrainPerSecond * dt, 0.1f);
            speed = sprintSpeed;
            IsSprinting = true;
        }

        CurrentMaxSpeed = speed;

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
        {
            landImpulse = Mathf.Abs(velocity.y);
            velocity.y = groundedGravity;
        }
    }

    private void GroundCheck(bool isReplayed, uint currentTick)
    {
        bool needInitialCheck = (lastGroundTick == currentTick && currentTick == 0);

        bool wasGrounded = currentTick - lastGroundTick <= coyoteTicks;
        float radius = (velocity.y < 0f) ? fallSphereRadius : groundSphereRadius;
        Vector3 origin = transform.position + Vector3.up * radius;
        Vector3 direction = Vector3.down;
        bool hit = Physics.SphereCast(
            origin,
            radius,
            direction,
            out RaycastHit _,
            groundCheckDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
        if (hit)
        {
            if (!wasGrounded && velocity.y <= 0f && !needInitialCheck)
            {
                pAnimations.TriggerLand();
                if (IsOwner && !isReplayed)
                {
                    pCamera.AddViewPunch(Vector3.right * landImpulse * 5f);
                }
            }
            lastGroundTick = currentTick;
        }
    }

    private void ApplyGravity(float dt, uint currentTick)
    {
        bool grounded = currentTick - lastGroundTick <= coyoteTicks;
        if (grounded && velocity.y < groundedGravity)
            velocity.y = groundedGravity;
        else
            velocity.y += gravity * dt;
    }

    public void ConsumeStamina(float amount, float regenDelaySeconds = 0f)
    {
        if (!IsOwner && !IsServerStarted) return;

        _currentStamina = Mathf.Max(0f, _currentStamina - amount);

        if (_currentStamina <= 1f)
        {
            _regenDelayRemaining = depletedRegenDelaySeconds;
        }
        else if (regenDelaySeconds > 0f)
        {
            _regenDelayRemaining = regenDelaySeconds;
        }

    }

    public void AddStamina(float amount)
    {
        if (!IsOwner && !IsServerStarted) return;

        _currentStamina = Mathf.Min(maxStamina, _currentStamina + amount);
        _regenDelayRemaining = 0f;
    }

    public bool IsStunned(uint currentTick)
    {
        return currentTick >= _stunStartTick && currentTick <= _stunEndTick;
    }

    public bool IsStunPending(uint currentTick)
    {
        return currentTick < _stunStartTick && _stunStartTick != uint.MaxValue;
    }

    [Server]
    public void ApplyStun(Vector3 stunnerPosition)
    {
        uint currentTick = TimeManager.Tick;

        if (currentTick >= _stunStartTick && currentTick <= _stunEndTick)
            return;

        uint futureTicks = TimeManager.TimeToTicks(0.15f, TickRounding.RoundUp);
        uint startTick = currentTick + futureTicks;
        uint endTick = startTick + stunTicks;

        if (ctx.Combat != null)
        {
            ctx.Combat.Server_StopAttackImmediate();
            ctx.Combat.Server_StopBlock();
        }

        ObserversApplyStun(stunnerPosition, startTick, endTick);
    }

    [ObserversRpc(RunLocally = true)]
    private void ObserversApplyStun(Vector3 stunnerPosition, uint serverStartTick, uint serverEndTick)
    {
        _stunStartTick = TimeManager.TickToLocalTick(serverStartTick);
        _stunEndTick = TimeManager.TickToLocalTick(serverEndTick);
        _stunnerPosition = stunnerPosition;

        retreatDirection = (transform.position - stunnerPosition).normalized;

        _retreatStartTick = _stunStartTick;
        _retreatEndTick = _stunStartTick + retreatTicks;

        if (base.IsOwner)
        {
            PlayerCombat combat = ctx.Combat;
            if (combat != null)
            {
                combat.LocalCancelAttackPresentation();
                combat.SetLocalCombatState(PlayerCombat.CombatState.Idle); // нужно добавить публичный метод
            }
        }
    }

    public Vector3 GetFlatVelocity() => flatVelocity;

    public bool IsGrounded { get; private set; }

    public float CurrentStamina => _currentStamina;
    public float MaxStamina => maxStamina;
}