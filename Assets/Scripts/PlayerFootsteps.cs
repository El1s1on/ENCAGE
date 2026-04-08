using FishNet.Object;
using FishNet.Transporting;
using System.Runtime.CompilerServices;
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

    private PlayerMovement pMovement;
    private float distanceAccumulator;
    private bool _wasGrounded;

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
            TriggerLandStep();
            distanceAccumulator = 0f;
        }

        _wasGrounded = true;

        Vector3 flatVel = pMovement.GetFlatVelocity();
        float speed = flatVel.magnitude;

        if (speed < 0.1f)
        {
            distanceAccumulator = 0f;
            return;
        }

        float dt = (float)TimeManager.TickDelta;
        bool isSprinting = pMovement.IsSprinting;
        float currentStepDistance = isSprinting ? sprintStepDistance : walkStepDistance;

        float distanceThisTick = speed * dt;
        distanceAccumulator += distanceThisTick;

        if (distanceAccumulator >= currentStepDistance)
        {
            TriggerFootstep();
            distanceAccumulator -= currentStepDistance;
        }
    }

    private void TriggerFootstep()
    {
        if (footstepClips == null || footstepClips.Length == 0 || !pMovement.IsGrounded) return;

        int clipIndex = Random.Range(0, footstepClips.Length);
        AudioClip clip = footstepClips[clipIndex];

        if (IsOwner)
            PlayLocalFootstep(clip, false);
        
        if (IsServerStarted)
        {
            RpcPlayFootstep(clipIndex, false);
        }  
    }

    private void TriggerLandStep()
    {
        if (footstepClips == null || footstepClips.Length == 0) return;

        int clipIndex = Random.Range(0, footstepClips.Length);
        AudioClip clip = footstepClips[clipIndex];

        if (IsOwner)
            PlayLocalFootstep(clip, true);

        if (IsServerStarted)
            RpcPlayFootstep(clipIndex, true);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcPlayFootstep(int clipIndex, bool isLand, Channel channel = Channel.Unreliable)
    {
        if (footstepClips == null || clipIndex < 0 || clipIndex >= footstepClips.Length) return;

        PlayLocalFootstep(footstepClips[clipIndex], isLand);
    }

    private void PlayLocalFootstep(AudioClip clip, bool isLand)
    {
        if (footstepSource == null || clip == null) return;

        if (isLand)
        {
            footstepSource.pitch = Random.Range(landPitchMin, landPitchMax);
        }
        else
        {
            footstepSource.pitch = Random.Range(pitchVariationMin, pitchVariationMax);
        }

        footstepSource.PlayOneShot(clip);
    }
}