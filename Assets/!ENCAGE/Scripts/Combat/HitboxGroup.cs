using Drawing;
using FishNet.Object;
using RootMotion.FinalIK;
using UnityEngine;

public enum HitboxType
{
    Head,
    Torso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg
}

public class HitboxGroup : NetworkBehaviour
{
    [System.Serializable]
    public struct HitboxEntry
    {
        public HitboxType type;
        public Collider collider;
        public Collider reactionCollider;
        public float damageMultipliers;
    }

    [SerializeField] private HitboxEntry[] hitboxes;
    [SerializeField] private HitReaction hitReaction;
    [SerializeField] private float hitForceMultiplier = 1f;

    [Server]
    public void ServerTriggerHitReaction(Collider hitCollider, Vector3 hitDirection, Vector3 hitPoint, float damage)
    {
        if (hitReaction == null)
        {
            Debug.LogWarning($"[HitboxGroup] hitReaction is NULL on {gameObject.name}");
            return;
        }

        if (hitCollider == null || hitCollider.transform == null)
        {
            Debug.LogWarning($"[HitboxGroup] HitCollider or its transform is NULL/Destroyed on {gameObject.name}");
            return;
        }

        if (!TryGetHitboxIndex(hitCollider, out int index))
        {
            Debug.LogWarning($"[HitboxGroup] Hit collider {hitCollider?.name} not found on {gameObject.name}");
            return;
        }

        HitboxEntry entry = hitboxes[index];
        Collider reactionCol = entry.reactionCollider != null ? entry.reactionCollider : entry.collider;

        // Проверяем что reactionCollider живой
        if (reactionCol == null || reactionCol.transform == null)
        {
            Debug.LogWarning($"[HitboxGroup] ReactionCollider or its transform is NULL/Destroyed | Index: {index} | Type: {entry.type}");
            return;
        }

        float hitboxForceMultiplier = GetHitboxForceMultiplier(entry.type);
        float totalForce = damage * hitForceMultiplier * hitboxForceMultiplier;
        Vector3 force = hitDirection.normalized * totalForce;

        Debug.Log($"[SERVER] HitReaction | {gameObject.name} | Type: {entry.type} | HitCol: {hitCollider.name} | ReactionCol: {reactionCol.name} | Force: {totalForce:F1} | Dir: {hitDirection.normalized} | HitPoint: {hitPoint}");


        try
        {
            hitReaction.Hit(reactionCol, force, hitPoint);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[HitboxGroup] Exception in HitReaction.Hit: {e.Message}\n{reactionCol.name}, force={force}, hitPoint={hitPoint}");
        }

        // Всем наблюдателям
        ObserversHitReaction(index, force, hitPoint);
    }

    private float GetHitboxForceMultiplier(HitboxType type)
    {
        return type switch
        {
            HitboxType.Head => 1.5f,
            HitboxType.Torso => 1.0f,
            HitboxType.LeftArm or HitboxType.RightArm => 0.7f,
            HitboxType.LeftLeg or HitboxType.RightLeg => 0.5f,
            _ => 1.0f,
        };
    }

    [ObserversRpc(BufferLast = false)]
    private void ObserversHitReaction(int hitboxIndex, Vector3 force, Vector3 hitPoint)
    {
        if (IsServerInitialized) return;
        if (hitReaction == null) return;
        if (hitboxIndex < 0 || hitboxIndex >= hitboxes.Length) return;

        HitboxEntry entry = hitboxes[hitboxIndex];
        Collider reactionCol = entry.reactionCollider != null ? entry.reactionCollider : entry.collider;

        if (reactionCol == null || reactionCol.transform == null)
        {
            Debug.LogWarning($"[RPC] ReactionCollider is NULL/Destroyed | Index: {hitboxIndex}");
            return;
        }

        try
        {
            hitReaction.Hit(reactionCol, force, hitPoint);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RPC] Exception: {e.Message}");
        }
    }

    public bool TryGetHitbox(Collider collider, out HitboxEntry entry)
    {
        for (int i = 0; i < hitboxes.Length; i++)
        {
            if (hitboxes[i].collider == collider)
            {
                entry = hitboxes[i];
                return true;
            }
        }
        entry = default;
        return false;
    }

    public bool TryGetHitboxIndex(Collider collider, out int index)
    {
        for (int i = 0; i < hitboxes.Length; i++)
        {
            if (hitboxes[i].collider == collider)
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    public float GetDamageMultiplier(Collider collider)
    {
        if (TryGetHitbox(collider, out HitboxEntry entry))
            return entry.damageMultipliers;
        return 1f;
    }

    public bool TryGetClosestHitbox(Vector3 point, out HitboxEntry entry)
    {
        entry = default;
        float bestDist = float.MaxValue;
        bool found = false;

        foreach (var hb in hitboxes)
        {
            if (hb.collider == null || !hb.collider.enabled)
                continue;

            Vector3 closest = hb.collider.ClosestPoint(point);
            float dist = (closest - point).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                entry = hb;
                found = true;
            }
        }
        return found;
    }

    public HitboxEntry[] GetAllHitboxes() => hitboxes;
}