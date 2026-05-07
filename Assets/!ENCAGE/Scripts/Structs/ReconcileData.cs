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
    // lastStunTick больше не нужен, удален
    public Vector3 stunnerPos;      // Позиция оглушившего
    public bool stunActive;        // Активен ли стан сейчас
    public uint stunStartTick;  // серверный тик начала стана
    public uint stunEndTick;
    public Vector3 retreatDirection;
    public Vector3 standupPos;
    public Quaternion standupRot;
    public bool PerformStandup;
    public float stamina;
    public float regenDelay;

    private uint _tick;

    // Конструктор
    public ReconcileData(Vector3 pos, Vector3 vel, Vector3 flatVel, Quaternion rot, float pitch,
        bool isRagdolled, uint groundTick,
        Vector3 stunnerPos, bool stunActive, uint stunStart, uint stunEnd, Vector3 retreatDirection,
        Vector3 standupPos, Quaternion standupRot, bool performStandup,
        float stamina, float regenDelay) : this()
    {
        this.pos = pos;
        this.vel = vel;
        this.flatVel = flatVel;
        this.rot = rot;
        this.pitch = pitch;
        this.IsRagdolled = isRagdolled;
        this.groundTick = groundTick;
        this.stunnerPos = stunnerPos;
        this.stunActive = stunActive;
        this.retreatDirection = retreatDirection;
        this.standupPos = standupPos;
        this.standupRot = standupRot;
        this.PerformStandup = performStandup;
        this.stamina = stamina;
        this.regenDelay = regenDelay;
        this.stunStartTick = stunStart;
        this.stunEndTick = stunEnd;
        this.stunActive = stunActive;
    }

    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
}