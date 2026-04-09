using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

public class PlayerFootsteps : NetworkBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private AudioSource footstepSource;

    [Header("Distances")]
    [SerializeField] private float walkStepDistance = 1.8f;
    [SerializeField] private float sprintStepDistance = 2.5f;

    [Header("Footstep Pitch")]
    [SerializeField, Range(0.8f, 1.2f)] private float pitchVariationMin = 0.95f;
    [SerializeField, Range(0.8f, 1.2f)] private float pitchVariationMax = 1.05f;

    [Header("Land Pitch")]
    [SerializeField, Range(0.6f, 1.0f)] private float landPitchMin = 0.75f;
    [SerializeField, Range(0.6f, 1.0f)] private float landPitchMax = 0.95f;

    [Header("Step Settings")]
    [SerializeField] private float idleResetTime = 0.5f;

    private PlayerMovement pMovement;
    private float distanceAccumulator;
    private bool _wasGrounded;
    private float _idleTime;
    private bool _nextFootIsLeft = true;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        pMovement = GetComponent<PlayerMovement>();
        if (pMovement == null)
        {
            Debug.LogError("PlayerFootsteps: PlayerMovement not found!", this);
            return;
        }

        _wasGrounded = pMovement.IsGrounded;

        if (Owner.IsLocalClient || IsServerStarted)
        {
            TimeManager.OnTick += OnTick;
        }
    }

    public override void OnStopNetwork()
    {
        if (Owner.IsLocalClient || IsServerStarted)
        {
            TimeManager.OnTick -= OnTick;
        }
        base.OnStopNetwork();
    }

    private void OnTick()
    {
        if (!IsOwner && !IsServerStarted) return;

        bool isGroundedNow = pMovement.IsGrounded;

        if (!isGroundedNow)
        {
            distanceAccumulator = 0f;
            _wasGrounded = false;
            return;
        }

        if (!_wasGrounded && isGroundedNow)
        {
            TriggerStep(true);
            distanceAccumulator = 0f;
        }

        _wasGrounded = true;

        Vector3 flatVel = pMovement.GetFlatVelocity();
        float speed = flatVel.magnitude;

        if (speed < 0.1f)
        {
            _idleTime += (float)TimeManager.TickDelta;
            if (_idleTime >= idleResetTime)
            {
                distanceAccumulator = 0f;
                _idleTime = 0f;
            }
            return;
        }

        _idleTime = 0f;

        float dt = (float)TimeManager.TickDelta;
        bool isSprinting = pMovement.IsSprinting;
        float currentStepDistance = isSprinting ? sprintStepDistance : walkStepDistance;

        float distanceThisTick = speed * dt;
        distanceAccumulator += distanceThisTick;

        if (distanceAccumulator >= currentStepDistance)
        {
            TriggerStep(false);
            distanceAccumulator -= currentStepDistance;
        }
    }

    private void TriggerStep(bool isLand)
    {
        if (footstepClips == null || footstepClips.Length == 0) return;

        if (!isLand && !pMovement.IsGrounded) return;

        int clipIndex = Random.Range(0, footstepClips.Length);
        AudioClip clip = footstepClips[clipIndex];

        bool isLeft = _nextFootIsLeft;
        _nextFootIsLeft = !_nextFootIsLeft;

        if (IsOwner)
            PlayLocalFootstep(clip, isLand, isLeft);

        if (IsServerStarted)
            RpcPlayFootstep(clipIndex, isLand, isLeft);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcPlayFootstep(int clipIndex, bool isLand, bool isLeft, Channel channel = Channel.Unreliable)
    {
        if (footstepClips == null || clipIndex < 0 || clipIndex >= footstepClips.Length) return;
        PlayLocalFootstep(footstepClips[clipIndex], isLand, isLeft);
    }

    private void PlayLocalFootstep(AudioClip clip, bool isLand, bool isLeft)
    {
        if (footstepSource == null || clip == null) return;

        footstepSource.pitch = isLand
            ? Random.Range(landPitchMin, landPitchMax)
            : Random.Range(pitchVariationMin, pitchVariationMax);

        if (IsOwner)
            footstepSource.panStereo = isLeft ? -0.8f : 0.8f;
        else
            footstepSource.panStereo = 0f;

        footstepSource.PlayOneShot(clip);
    }
}