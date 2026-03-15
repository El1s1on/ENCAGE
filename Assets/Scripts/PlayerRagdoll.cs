using FishNet.Component.Animating;
using FishNet.Component.Transforming.Beta;
using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;
public class PlayerRagdoll : NetworkBehaviour
{
    [SerializeField] private Transform cameraPositionTransform;
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private Transform headTransform;
    [SerializeField] private Rigidbody pelvisRigidbody;

    [Header("Ragdoll Stand-up")]
    [SerializeField] private LayerMask groundMask;

    [Header("Sync Settings")]
    [SerializeField] private float syncIntervalSeconds = 0.067f;
    [SerializeField] private float sleepThreshold = 0.03f;

    private PlayerMovement playerMovement;
    private Animator animator;
    private NetworkAnimator networkAnimator;
    private NetworkTickSmoother tickSmoother;
    private List<Rigidbody> allRigidbodies = new List<Rigidbody>();
    private CharacterController characterController;

    private Vector3 originalBodyLocalPos;
    private Quaternion originalBodyLocalRot;

    public Vector3 PendingStandupPosition;
    public Quaternion PendingStandupRotation;

    private bool _isRagdolled;
    public bool IsRagdolled => _isRagdolled;

    private bool _pendingStandup;                    // ← новый флаг
    public bool HasPendingStandup => _pendingStandup;

    // Стейт для интерполяции снапшотов
    private float timeSinceLastSync = 0f;
    private Vector3[] lastPositions;
    private Quaternion[] lastRotations;
    private Vector3[] nextPositions;
    private Quaternion[] nextRotations;
    private float snapshotTime = 0f;
    private bool wasAsleep = false;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        playerMovement = GetComponent<PlayerMovement>();
        if (bodyTransform == null) bodyTransform = transform.Find("Graphical Object/Body");

        animator = bodyTransform.GetComponentInChildren<Animator>(true);
        networkAnimator = bodyTransform.GetComponentInChildren<NetworkAnimator>(true);
        tickSmoother = bodyTransform.parent?.GetComponent<NetworkTickSmoother>();
        characterController = GetComponent<CharacterController>();

        CollectAllRigidbodies();
        TimeManager.OnPostTick += TimeManager_OnPostTick;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        TimeManager.OnPostTick -= TimeManager_OnPostTick;
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
    public void ActivateRagdoll()
    {
        SetIsRagdolled(true);
    }

    [Server]
    public void DeactivateRagdoll()
    {
        if (!_isRagdolled) return;

        MoveRootToPelvisServer();   // рассчитываем позицию стендапа
        _pendingStandup = true;     // поднимаем флаг
    }

    // ====================== ВНУТРЕННЯЯ ЛОГИКА ======================
    public void SetIsRagdolled(bool newValue)
    {
        if (_isRagdolled == newValue) return;

        bool oldValue = _isRagdolled;
        _isRagdolled = newValue;

        if (!newValue) _pendingStandup = false;   // очищаем флаг при выходе

        OnRagdollStateChanged(oldValue, newValue, base.IsServerInitialized);
    }

    [Server]
    private void MoveRootToPelvisServer()
    {
        if (pelvisRigidbody == null) return;

        Vector3 targetPos = pelvisRigidbody.position;

        if (Physics.Raycast(targetPos + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 2f, groundMask))
        {
            targetPos.y = hit.point.y + characterController.skinWidth + 0.05f; // +0.05f вместо 0.01f — запас
        }

        PendingStandupPosition = targetPos;

        // ← ФИКС РОТАЦИИ (было криво, если pelvis наклонён)
        Vector3 forward = pelvisRigidbody.transform.forward;
        forward.y = 0f;
        PendingStandupRotation = forward.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(forward)
            : transform.rotation;

        Debug.DrawLine(targetPos, targetPos + Vector3.up * 0.1f, Color.red, 5f);
        Debug.Log("SERVER STANDUP SET " + targetPos);
    }

    private void OnRagdollStateChanged(bool oldValue, bool newValue, bool asServer)
    {
        if (playerMovement != null) playerMovement.SetControllerEnabled(!newValue);
        if (animator != null) animator.enabled = !newValue;
        if (networkAnimator != null) networkAnimator.enabled = !newValue;

        if (newValue) // ВХОД В RAGDOLL
        {
            timeSinceLastSync = 0f;
            snapshotTime = 0f;
            wasAsleep = false;

            for (int i = 0; i < allRigidbodies.Count; i++)
            {
                var rb = allRigidbodies[i];
                if (asServer)
                {
                    rb.isKinematic = false;
                    rb.useGravity = true;
                }
                else
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
                lastPositions[i] = rb.position;
                lastRotations[i] = rb.rotation;
                nextPositions[i] = rb.position;
                nextRotations[i] = rb.rotation;
            }

            if (asServer && pelvisRigidbody != null && playerMovement != null)
                pelvisRigidbody.linearVelocity = playerMovement.GetVelocity();

            cameraPositionTransform.SetParent(headTransform);
            if (tickSmoother != null) tickSmoother.enabled = false;
        }
        else // ВЫХОД ИЗ RAGDOLL
        {
            foreach (var rb in allRigidbodies)
            {
                if (asServer) rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            cameraPositionTransform.SetParent(bodyTransform.parent);
            cameraPositionTransform.localPosition = new Vector3(0, 1.695f, 0);
            cameraPositionTransform.localRotation = Quaternion.identity;
            if (tickSmoother != null) tickSmoother.enabled = true;
        }
    }

    public void ResetBonesToRoot()
    {
        bodyTransform.localPosition = originalBodyLocalPos;
        bodyTransform.localRotation = originalBodyLocalRot;
    }

    public void EnableTickSmoother(bool enabled)
    {
        if (tickSmoother != null) tickSmoother.enabled = enabled;
    }

    private void TimeManager_OnPostTick()
    {
        if (!_isRagdolled || !IsServerInitialized)
            return;

        timeSinceLastSync += (float)TimeManager.TickDelta;
        if (timeSinceLastSync >= syncIntervalSeconds)
        {
            bool isAsleep = IsAllAsleep();
            if (!isAsleep || !wasAsleep)
            {
                SendSnapshot();
            }
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
        {
            if (rb.linearVelocity.sqrMagnitude > sleepThreshold * sleepThreshold)
                return false;
        }
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

    private void Update()
    {
        if (!_isRagdolled || IsServerInitialized)
            return;
        snapshotTime += Time.deltaTime;
        float t = Mathf.Clamp01(snapshotTime / syncIntervalSeconds);
        for (int i = 0; i < allRigidbodies.Count; i++)
        {
            var rb = allRigidbodies[i];
            rb.position = Vector3.Lerp(lastPositions[i], nextPositions[i], t);
            rb.rotation = Quaternion.Lerp(lastRotations[i], nextRotations[i], t);
        }
    }
}