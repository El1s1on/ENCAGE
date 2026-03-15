using FishNet.Object;
using UnityEngine;

public class RagdollSync : NetworkBehaviour
{
    [SerializeField] private float syncIntervalSeconds = 0.067f;
    [SerializeField] private float sleepThreshold = 0.03f;

    private PlayerRagdoll playerRagdoll;
    private Rigidbody[] ragdollBodies;
    private float timeSinceLastSync = 0f;
    private bool isRagdollActive = false;

    private Vector3[] lastPositions;
    private Quaternion[] lastRotations;
    private Vector3[] nextPositions;
    private Quaternion[] nextRotations;
    private float snapshotTime = 0f;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        playerRagdoll = GetComponent<PlayerRagdoll>();
        ragdollBodies = GetComponentsInChildren<Rigidbody>();

        int boneCount = ragdollBodies.Length;
        lastPositions = new Vector3[boneCount];
        lastRotations = new Quaternion[boneCount];
        nextPositions = new Vector3[boneCount];
        nextRotations = new Quaternion[boneCount];

        TimeManager.OnPostTick += TimeManager_OnPostTick;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        TimeManager.OnPostTick -= TimeManager_OnPostTick;
    }

    public void OnRagdollActivated()
    {
        isRagdollActive = true;
        timeSinceLastSync = 0f;
        snapshotTime = 0f;

        for (int i = 0; i < ragdollBodies.Length; i++)
        {
            var rb = ragdollBodies[i];

            if (IsServerInitialized)
            {
                // Сервер: полная физика
                rb.isKinematic = false;
                rb.useGravity = true;
            }
            else
            {
                // Клиент: kinematic, никаких velocity!
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            lastPositions[i] = rb.position;
            lastRotations[i] = rb.rotation;
            nextPositions[i] = rb.position;
            nextRotations[i] = rb.rotation;
        }
    }

    private void TimeManager_OnPostTick()
    {
        if (!playerRagdoll.IsRagdolled || !isRagdollActive)
            return;

        if (!IsServerInitialized)
            return;

        timeSinceLastSync += (float)TimeManager.TickDelta;

        if (timeSinceLastSync >= syncIntervalSeconds)
        {
            SendSnapshot();
            timeSinceLastSync = 0f;

            if (IsAllAsleep())
            {
                DeactivateRagdoll();
            }
        }
    }

    private void SendSnapshot()
    {
        for (int i = 0; i < ragdollBodies.Length; i++)
        {
            lastPositions[i] = ragdollBodies[i].position;
            lastRotations[i] = ragdollBodies[i].rotation;
        }

        ReceiveSnapshotObserversRpc(lastPositions, lastRotations);
    }

    private bool IsAllAsleep()
    {
        foreach (var rb in ragdollBodies)
        {
            if (rb.linearVelocity.sqrMagnitude > sleepThreshold * sleepThreshold)
                return false;
        }
        return true;
    }

    [ObserversRpc]
    private void ReceiveSnapshotObserversRpc(Vector3[] positions, Quaternion[] rotations)
    {
        if (IsServerInitialized)
            return; // Сервер не получает свои же RPC

        // На клиентах: сдвигаем снапшоты
        for (int i = 0; i < ragdollBodies.Length; i++)
        {
            lastPositions[i] = nextPositions[i];
            lastRotations[i] = nextRotations[i];
            nextPositions[i] = positions[i];
            nextRotations[i] = rotations[i];
        }

        snapshotTime = 0f;
    }

    private void Update()
    {
        if (!playerRagdoll.IsRagdolled || !isRagdollActive)
            return;

        if (IsServerInitialized)
            return; // Сервер не интерполирует

        snapshotTime += Time.deltaTime;
        float t = Mathf.Clamp01(snapshotTime / syncIntervalSeconds);

        for (int i = 0; i < ragdollBodies.Length; i++)
        {
            var rb = ragdollBodies[i];

            // Только position и rotation, никаких velocity!
            rb.position = Vector3.Lerp(lastPositions[i], nextPositions[i], t);
            rb.rotation = Quaternion.Lerp(lastRotations[i], nextRotations[i], t);
        }
    }

    [ServerRpc]
    private void DeactivateRagdoll()
    {
        DeactivateRagdollObserversRpc();
    }

    [ObserversRpc]
    private void DeactivateRagdollObserversRpc()
    {
        foreach (var rb in ragdollBodies)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero; // ← Это OK, кость уже kinematic
        }
        isRagdollActive = false;
    }
}