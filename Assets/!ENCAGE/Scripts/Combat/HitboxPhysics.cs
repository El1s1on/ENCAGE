using FishNet.Object;
using UnityEngine;

public static class HitboxPhysics
{
    public readonly struct Impact
    {
        public readonly bool IsWorld;
        public readonly int TargetObjectId;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;

        public Impact(bool isWorld, int targetObjectId, Vector3 point, Vector3 normal)
        {
            IsWorld = isWorld;
            TargetObjectId = targetObjectId;
            Point = point;
            Normal = normal;
        }
    }

    /// <summary>
    /// Returns the correct point and normal on the collider surface upon intersection/overlap.
    /// Used for both sweep and OverlapSphere/OverlapCapsule.
    /// </summary>
    public static bool TryGetOverlapSurfaceHit(
        Collider c,
        Vector3 fromPoint,
        Vector3 attackOrigin,
        Transform fallbackForward,
        out Vector3 hitPoint,
        out Vector3 hitNormal
    )
    {
        hitPoint = fromPoint;
        hitNormal = fallbackForward != null ? -fallbackForward.forward : Vector3.up;

        if (c == null) return true;

        Vector3 cp = c.ClosestPoint(fromPoint);
        float distToCp = (cp - fromPoint).magnitude;

        if (distToCp > 1.5f)
        {
            Vector3 attackDir = (fromPoint - attackOrigin).normalized;
            if (attackDir.sqrMagnitude < 0.01f && fallbackForward != null)
                attackDir = -fallbackForward.forward;

            Vector3 backDir = -attackDir;

            Ray ray = new Ray(fromPoint, backDir);
            float maxDist = c.bounds.extents.magnitude * 10f;

            if (c.Raycast(ray, out RaycastHit hit, maxDist))
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                return true;
            }

            if (Physics.ComputePenetration(
                    new SphereCollider { radius = 0.01f, center = fromPoint },
                    fromPoint,
                    Quaternion.identity,
                    c,
                    c.transform.position,
                    c.transform.rotation,
                    out Vector3 direction,
                    out float distance))
            {
                hitPoint = fromPoint + direction * distance;
                hitNormal = direction;
                return true;
            }
        }

        if (distToCp > 0.0001f)
        {
            hitPoint = cp;
            hitNormal = (fromPoint - cp).normalized;
            return true;
        }

        hitPoint = cp;
        return true;
    }

    /// <summary>
    /// Core hit validation logic: checks whether the hit is blocked by a wall/geometry.
    /// Returns Impact (world or target).
    /// </summary>
    public static bool ResolveImpact(
        NetworkBehaviour ownerBehaviour,
        Vector3 origin,
        Collider hitCollider,
        Vector3 proposedPoint,
        Vector3 proposedNormal,
        LayerMask occlusionMask,
        out Impact impact
    )
    {
        impact = default;

        if (hitCollider == null) return false;

        NetworkObject selfNob = ownerBehaviour != null ? ownerBehaviour.NetworkObject : null;
        NetworkObject targetNob = hitCollider.GetComponentInParent<NetworkObject>();

        if (selfNob != null && targetNob == selfNob) return false;

        bool isNetworkTarget = targetNob != null;
        int targetObjectId = isNetworkTarget ? targetNob.ObjectId : -1;

        if (origin == default)
        {
            impact = new Impact(!isNetworkTarget, targetObjectId, proposedPoint, proposedNormal);
            return true;
        }

        Vector3 dir = proposedPoint - origin;
        float dist = dir.magnitude;
        if (dist <= 0.0001f)
        {
            impact = new Impact(!isNetworkTarget, targetObjectId, proposedPoint, proposedNormal);
            return true;
        }

        dir /= dist;
        float rayDist = Mathf.Max(0f, dist - 0.01f);

        RaycastHit[] hits = new RaycastHit[16];
        int hitCount = Physics.RaycastNonAlloc(origin, dir, hits, rayDist, occlusionMask, QueryTriggerInteraction.Ignore);

        if (hitCount > 0)
        {
            int bestIndex = -1;
            float bestDist = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                Collider c = hits[i].collider;
                if (c == null) continue;

                NetworkObject hitNob = c.GetComponentInParent<NetworkObject>();
                if (selfNob != null && hitNob == selfNob) continue;

                float d = hits[i].distance;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIndex = i;
                }
            }

            if (bestIndex == -1)
            {
                impact = new Impact(!isNetworkTarget, targetObjectId, proposedPoint, proposedNormal);
                return true;
            }

            Collider first = hits[bestIndex].collider;

            if (isNetworkTarget && hitCollider != null &&
                (first == hitCollider ||
                 first.transform.IsChildOf(hitCollider.transform) ||
                 hitCollider.transform.IsChildOf(first.transform)))
            {
                impact = new Impact(false, targetObjectId, proposedPoint, proposedNormal);
                return true;
            }

            RaycastHit rh = hits[bestIndex];
            impact = new Impact(true, -1, rh.point, rh.normal);
            return true;
        }

        impact = isNetworkTarget && hitCollider != null
            ? new Impact(false, targetObjectId, proposedPoint, proposedNormal)
            : new Impact(true, -1, proposedPoint, proposedNormal);

        return true;
    }
}