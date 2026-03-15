using FishNet.Object.Prediction;
using UnityEngine;

public struct ReconcileData : IReconcileData
{
    public Vector3 pos;
    public Vector3 vel;
    public Vector3 flatVel;
    public Quaternion rot;
    public bool IsRagdolled;
    public uint groundTick;
    public Vector3 standupPos;
    public Quaternion standupRot;
    public bool PerformStandup;          // ← главный флаг вместо проверки Vector3.zero

    public ReconcileData(
        Vector3 pos,
        Vector3 vel,
        Vector3 flatVel,
        Quaternion rot,
        bool isRagdolled,
        uint groundTick,
        Vector3 standupPos,
        Quaternion standupRot,
        bool performStandup) : this()
    {
        this.pos = pos;
        this.vel = vel;
        this.flatVel = flatVel;
        this.rot = rot;
        this.IsRagdolled = isRagdolled;
        this.groundTick = groundTick;
        this.standupPos = standupPos;
        this.standupRot = standupRot;
        this.PerformStandup = performStandup;
    }

    private uint _tick;
    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
}