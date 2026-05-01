using FishNet.Component.Animating;
using FishNet.Component.Transforming.Beta;
using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;
using FishNet.Object.Synchronizing;
using Unity.Collections.LowLevel.Unsafe;
using FishNet.Connection;

public class PlayerRagdoll : NetworkBehaviour
{
    [SerializeField] private Transform cameraPositionTransform;
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform eyesTransform;
    [SerializeField] private Rigidbody pelvisRigidbody;

    [Header("Ragdoll Stand-up")]
    [SerializeField] private LayerMask groundMask;

    [Header("Sync Settings")]
    [SerializeField] private float syncIntervalSeconds = 0.067f;
    [SerializeField] private float sleepThreshold = 0.03f;

    private PlayerMovement playerMovement;
    private PlayerCamera playerCamera;
    private PlayerAnimations pAnimations;
    private Animator animator;
    private NetworkAnimator networkAnimator;
    private NetworkTickSmoother tickSmoother;
    private SkinnedMeshRenderer meshRenderer;
    private List<Rigidbody> allRigidbodies = new List<Rigidbody>();
    private CharacterController characterController;

    private float originalCameraHeight;
    private Vector3 originalBodyLocalPos;
    private Quaternion originalBodyLocalRot;

    [HideInInspector] public Vector3 PendingStandupPosition;
    [HideInInspector] public Quaternion PendingStandupRotation;
    [HideInInspector] public bool HasPendingStandup;

    private readonly SyncVar<bool> _isRagdolled = new SyncVar<bool>();
    public bool IsRagdolled => _isRagdolled.Value;

    private Vector3 _pendingRagdollImpulse;
    private float timeSinceLastSync = 0f;
    private Vector3[] lastPositions;
    private Quaternion[] lastRotations;
    private Vector3[] nextPositions;
    private Quaternion[] nextRotations;
    private float snapshotTime = 0f;
    private bool wasAsleep = false;
    private bool _forceSnapNextLateUpdate = false;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        playerMovement = GetComponent<PlayerMovement>();
        playerCamera = GetComponent<PlayerCamera>();
        pAnimations = GetComponent<PlayerAnimations>();
        if (bodyTransform == null) bodyTransform = transform.Find("Graphical Object/Body");

        animator = bodyTransform.GetComponentInChildren<Animator>(true);
        networkAnimator = bodyTransform.GetComponentInChildren<NetworkAnimator>(true);
        tickSmoother = bodyTransform.parent?.GetComponent<NetworkTickSmoother>();
        characterController = GetComponent<CharacterController>();
        meshRenderer = bodyTransform.GetComponentInChildren<SkinnedMeshRenderer>();
        meshRenderer.enabled = !Owner.IsLocalClient;

        originalCameraHeight = cameraPositionTransform.localPosition.y;

        CollectAllRigidbodies();
        TimeManager.OnPostTick += TimeManager_OnPostTick;
        _isRagdolled.OnChange += OnRagdollStateChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        TimeManager.OnPostTick -= TimeManager_OnPostTick;
        _isRagdolled.OnChange -= OnRagdollStateChanged;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
            headTransform.localScale = Vector3.zero;

        if (_isRagdolled.Value && !IsServerInitialized)
        {
            RequestInitialSnapshotServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInitialSnapshotServerRpc(NetworkConnection conn = null)
    {
        SendSnapshotTargetRpc(conn);
    }

    [TargetRpc]
    private void SendSnapshotTargetRpc(NetworkConnection conn)
    {
        ReceiveSnapshotObserversRpc(lastPositions, lastRotations);
    }

    private void CollectAllRigidbodies()
    {
        originalBodyLocalPos = bodyTransform.localPosition;
        originalBodyLocalRot = bodyTransform.localRotation;

        var allRbs = bodyTransform.GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in allRbs)
        {
            allRigidbodies.Add(rb);
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        int boneCount = allRigidbodies.Count;
        lastPositions = new Vector3[boneCount];
        lastRotations = new Quaternion[boneCount];
        nextPositions = new Vector3[boneCount];
        nextRotations = new Quaternion[boneCount];
    }

    [Server]
    public void ActivateRagdoll(Vector3 impulse = default)
    {
        _pendingRagdollImpulse = impulse;
        SetIsRagdolled(true);
        SendSnapshot();
    }

    [Server]
    public void DeactivateRagdoll()
    {
        if (!_isRagdolled.Value) return;
        MoveRootToPelvisServer();
        HasPendingStandup = true;
        _pendingRagdollImpulse = Vector3.zero;
    }

    private void ApplyRagdollImpulse(Vector3 impulse)
    {
        if (impulse == Vector3.zero) return;

        if (pelvisRigidbody != null)
            pelvisRigidbody.linearVelocity = impulse;

        foreach (var rb in allRigidbodies)
        {
            if (rb == pelvisRigidbody) continue;

            Vector3 extra = impulse;
            rb.AddForce(extra, ForceMode.VelocityChange);
        }
    }

    public void PerformInstantStandupSnap()
    {
        ResetBonesToRoot();

        for (int i = 0; i < allRigidbodies.Count; i++)
        {
            var rb = allRigidbodies[i];
            rb.position = rb.transform.position;
            rb.rotation = rb.transform.rotation;
        }

        if (tickSmoother != null && tickSmoother.SmootherController?.UniversalSmoother != null)
        {
            tickSmoother.SmootherController.UniversalSmoother.Teleport();
        }

        SnapRagdollBonesToCurrent();

        _forceSnapNextLateUpdate = true;
    }

    public void ResetBonesToRoot()
    {
        bodyTransform.localPosition = originalBodyLocalPos;
        bodyTransform.localRotation = originalBodyLocalRot;
    }

    public void SnapRagdollBonesToCurrent()
    {
        for (int i = 0; i < allRigidbodies.Count; i++)
        {
            lastPositions[i] = allRigidbodies[i].position;
            lastRotations[i] = allRigidbodies[i].rotation;
            nextPositions[i] = allRigidbodies[i].position;
            nextRotations[i] = allRigidbodies[i].rotation;
        }
        snapshotTime = 0f;
    }

    [Server]
    private void MoveRootToPelvisServer()
    {
        if (pelvisRigidbody == null) return;

        Vector3 targetPos = pelvisRigidbody.position;
        if (Physics.Raycast(targetPos + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 2f, groundMask))
        {
            targetPos.y = hit.point.y + characterController.skinWidth + 0.05f;
        }

        PendingStandupPosition = targetPos;

        Vector3 forward = pelvisRigidbody.transform.forward;
        forward.y = 0f;
        PendingStandupRotation = forward.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(forward)
            : transform.rotation;
    }

    public void SetIsRagdolled(bool newValue)
    {
        if (IsServerInitialized)
            _isRagdolled.Value = newValue;
    }

    private void OnRagdollStateChanged(bool oldValue, bool newValue, bool asServer)
    {
        if (playerMovement != null) playerMovement.SetControllerEnabled(!newValue);
        if (animator != null) animator.enabled = !newValue;
        if (networkAnimator != null) networkAnimator.enabled = !newValue;

        int ragdollLayer = LayerMask.NameToLayer("Ragdoll");
        int playerLayer = LayerMask.NameToLayer("Player");

        foreach (var rb in allRigidbodies)
            rb.gameObject.layer = newValue ? ragdollLayer : playerLayer;

        if (newValue)
        {
            timeSinceLastSync = 0f;
            snapshotTime = 0f;
            wasAsleep = false;

            for (int i = 0; i < allRigidbodies.Count; i++)
            {
                bool isAuthority = IsServerInitialized;

                var rb = allRigidbodies[i];
                rb.isKinematic = !isAuthority;
                if (asServer)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.useGravity = isAuthority;
                lastPositions[i] = rb.position;
                lastRotations[i] = rb.rotation;
                nextPositions[i] = rb.position;
                nextRotations[i] = rb.rotation;
            }

            Vector3 finalImpulse = _pendingRagdollImpulse;

            if (asServer || IsOwner)
                ApplyRagdollImpulse(finalImpulse);

            _pendingRagdollImpulse = Vector3.zero;

            meshRenderer.enabled = true;
            cameraPositionTransform.SetParent(eyesTransform, false);
            cameraPositionTransform.localPosition = Vector3.zero;
            cameraPositionTransform.localRotation = Quaternion.identity;

            Tween.LocalPosition(cameraPositionTransform, Vector3.zero, 0.2f, Ease.OutCubic);

            ForceRagdollSnap();
        }
        else
        {
            cameraPositionTransform.SetParent(bodyTransform.parent, false);

            Tween.LocalPosition(
                cameraPositionTransform,
                new Vector3(0, originalCameraHeight, 0),
                0.6f,
                Ease.OutCubic
            );

            foreach (var rb in allRigidbodies)
            {
                if (asServer) rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (IsOwner)
                meshRenderer.enabled = false;

            if (pAnimations != null)
            {
                pAnimations.ForceGroundedIdle();
            }

            if (IsOwner && playerCamera != null)
            {
                playerCamera.OnStandup();
            }
        }
    }

    public void ForceRagdollSnap()
    {
        for (int i = 0; i < allRigidbodies.Count; i++)
        {
            lastPositions[i] = allRigidbodies[i].position;
            lastRotations[i] = allRigidbodies[i].rotation;
            nextPositions[i] = allRigidbodies[i].position;
            nextRotations[i] = allRigidbodies[i].rotation;
        }
        snapshotTime = 0f;
    }

    private void TimeManager_OnPostTick()
    {
        if (!_isRagdolled.Value || !IsServerInitialized) return;

        timeSinceLastSync += (float)TimeManager.TickDelta;
        if (timeSinceLastSync >= syncIntervalSeconds)
        {
            bool isAsleep = IsAllAsleep();
            if (!isAsleep || !wasAsleep)
                SendSnapshot();

            wasAsleep = isAsleep;
            timeSinceLastSync = 0f;
        }
    }

    private void SendSnapshot()
    {
        for (int i = 0; i < allRigidbodies.Count; i++)
        {
            lastPositions[i] = allRigidbodies[i].position;
            lastRotations[i] = allRigidbodies[i].rotation;
        }
        ReceiveSnapshotObserversRpc(lastPositions, lastRotations);
    }

    private bool IsAllAsleep()
    {
        foreach (var rb in allRigidbodies)
            if (rb.linearVelocity.sqrMagnitude > sleepThreshold * sleepThreshold)
                return false;
        return true;
    }

    [ObserversRpc(ExcludeServer = true)]
    private void ReceiveSnapshotObserversRpc(Vector3[] positions, Quaternion[] rotations)
    {
        for (int i = 0; i < allRigidbodies.Count; i++)
        {
            lastPositions[i] = nextPositions[i];
            lastRotations[i] = nextRotations[i];
            nextPositions[i] = positions[i];
            nextRotations[i] = rotations[i];
        }
        snapshotTime = 0f;
    }

    private void LateUpdate()
    {
        if (_forceSnapNextLateUpdate)
        {
            Transform graphicalParent = bodyTransform.parent;
            if (graphicalParent != null)
            {
                graphicalParent.position = transform.position;
                graphicalParent.rotation = transform.rotation;
            }

            if (tickSmoother != null && tickSmoother.SmootherController?.UniversalSmoother != null)
            {
                tickSmoother.SmootherController.UniversalSmoother.Teleport();
            }

            _forceSnapNextLateUpdate = false;
        }

        if (IsServerInitialized || !_isRagdolled.Value) return;

        snapshotTime += Time.deltaTime;
        float t = Mathf.Clamp01(snapshotTime / syncIntervalSeconds);
        for (int i = 0; i < allRigidbodies.Count; i++)
        {
            var tr = allRigidbodies[i].transform;
            tr.position = Vector3.Lerp(lastPositions[i], nextPositions[i], t);
            tr.rotation = Quaternion.Lerp(lastRotations[i], nextRotations[i], t);
        }
    }
}