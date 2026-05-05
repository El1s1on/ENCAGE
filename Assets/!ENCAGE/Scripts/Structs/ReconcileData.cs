using FishNet.Object.Prediction;
using UnityEngine;

public struct ReconcileData : IReconcileData
{
    public Vector3 pos;
    public Vector3 vel;
    public Vector3 flatVel;
    public Quaternion rot;
    public float pitch;
    public bool IsRagdolled;
    public uint groundTick;
    public Vector3 standupPos;
    public Quaternion standupRot;
    public bool PerformStandup;
    public float stamina;
    public float regenDelay;

    public ReconcileData(
        Vector3 position, Vector3 velocity, Vector3 flatVelocity, Quaternion rotation, float clampedPitch,
        bool isRagdolled, uint groundTickVal,
        Vector3 standPos, Quaternion standRot, bool performStandup,
        float currentStamina, float currentRegenDelay
    ) : this()
    {
        pos = position; vel = velocity; flatVel = flatVelocity; rot = rotation; pitch = clampedPitch;
        IsRagdolled = isRagdolled; groundTick = groundTickVal;
        standupPos = standPos; standupRot = standRot; PerformStandup = performStandup;
        stamina = currentStamina;
        regenDelay = currentRegenDelay;
    }

    private uint _tick;
    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
}