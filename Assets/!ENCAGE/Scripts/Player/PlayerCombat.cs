using Drawing;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct HitData
{
    private GameObject obj;
    private Vector3 hitPoint;
    private PreciseTick preciseTick;

    public HitData(GameObject target, Vector3 point, PreciseTick tick)
    {
        obj = target;
        hitPoint = point;
        preciseTick = tick;
    }

    public GameObject TargetObj => obj;
    public Vector3 HitPoint => hitPoint;
    public PreciseTick PreciseTick => preciseTick;
}

public class PlayerCombat : NetworkBehaviour
{
    public enum CombatState
    {
        Idle,
        Holding,
        LightAttack,
        PreHeavyAttack,
        HeavyAttack,
        Blocking,
        UnblockQueue
    }

    [SerializeField] private Transform cameraPosition;

    [Header("FX")]
    [SerializeField] private NetworkObject concreteImpactFX;
    [SerializeField] private NetworkObject bloodImpactFX;

    [Header("Hitboxes")]
    [SerializeField] private Transform rightHandHitbox;
    [SerializeField] private Transform leftHandHitbox;

    [SerializeField] private LayerMask hitMask;
    [SerializeField] private LayerMask occlusionMask;

    private WeaponViewModel viewModel;
    public WeaponData currentWeapon;

    private PlayerContext ctx;
    private PlayerAnimations pAnimations;
    private PlayerMovement pMovement;
    private PlayerRagdoll pRagdoll;
    private PlayerCamera pCamera;
    private PlayerInput pInput;
    private PlayerUI pUI;

    private CombatState localCombatState = CombatState.Idle;

    private float startHoldingTime;
    private Coroutine chargeRoutine;
    private Coroutine localHitboxRoutine;

    private int direction = 1;
    private bool isBufferingNextAttack;
    private bool isHoldingForNextAttack;

    private float lastLocalBlockReleaseTime = -999f;
    private bool m2Held;
    private bool blockBuffered;

    private AttackData currentAttackData;
    private uint attackHoldStartTick;
    private uint attackExecuteTick;
    private uint attackEndTick;
    private bool isAttackReady;
    private bool isAttacking;
    private bool serverRegisteredDamageHitThisAttack;
    private int serverAttackDirection = 1;
    private Coroutine serverChargeRoutine;

    private uint blockStartTick;
    private bool isBlocking;
    private bool unblockQueue;

    [Header("Server Hit Moderation")]
    [SerializeField] private float serverBaseReach = 1.25f;
    [SerializeField] private float serverHitRangeSlack = 0.75f;
    [SerializeField] private uint serverHitWindowSlackTicks = 4;

    [Header("Combat Rules (Server)")]
    [SerializeField] private float heavyBlockDamageMultiplier = 0.5f;
    [SerializeField] private float backstabDamageMultiplier = 2f;

    #region Public Server API
    public bool ServerIsBlocking => IsServerInitialized && isBlocking;

    public uint ServerBlockStartTick => IsServerInitialized ? blockStartTick : 0u;

    private bool CanSendToOwner()
    {
        if (base.Owner == null)
            return false;

        if (!base.Owner.IsActive)
            return false;

        if (!base.Owner.IsValid)
            return false;

        if (!base.Observers.Contains(base.Owner))
            return false;

        return true;
    }

    [Server]
    public bool TryGetServerBlockInfo(out uint startTick)
    {
        startTick = blockStartTick;
        return isBlocking;
    }
    #endregion

    #region Public Server API
    public bool SharedIsBlocking => isBlocking || m2Held;
    #endregion

    #region Network Lifecycle
    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            viewModel = GetComponentInChildren<WeaponViewModel>();

            pInput.onStartM1 += OnStartM1;
            pInput.onReleaseM1 += OnReleaseM1;

            pInput.onStartM2 += OnStartM2;
            pInput.onReleaseM2 += OnReleaseM2;
        }
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        ctx = PlayerContext.Get(NetworkObject);
        pMovement = ctx.Movement;
        pAnimations = ctx.Animations;
        pRagdoll = ctx.Ragdoll;
        pUI = ctx.UI;
        pInput = ctx.Input;
        pCamera = ctx.Camera;

        InstanceFinder.NetworkManager.CacheObjects(concreteImpactFX, 10, IsServerInitialized);
        InstanceFinder.NetworkManager.CacheObjects(bloodImpactFX, 10, IsServerInitialized);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        if (pInput)
        {
            pInput.onStartM1 -= OnStartM1;
            pInput.onReleaseM1 -= OnReleaseM1;

            pInput.onStartM2 -= OnStartM2;
            pInput.onReleaseM2 -= OnReleaseM2;
        }
    }
    #endregion

    private void Update()
    {
        if (!IsServerInitialized) return;

        if (isAttacking && TimeManager.Tick >= attackEndTick)
            Server_StopAttack();

        if (unblockQueue && TimeManager.Tick >= blockStartTick + TimeManager.TimeToTicks(currentWeapon.defense.blockCooldown, TickRounding.RoundUp))
        {
            Server_StopBlock();
        }

        // Client-side buffered block: if player is holding M2 but block wasn't allowed
        // at press time, attempt to start it as soon as it becomes legal.
        if (IsOwner && blockBuffered && m2Held)
            TryConsumeBufferedBlock();
    }

    #region Player Input
    private void OnStartM1()
    {
        if (!IsOwner || pRagdoll.IsRagdolled) return;
        if (currentWeapon == null) return;

        if (localCombatState == CombatState.Blocking || localCombatState == CombatState.UnblockQueue)
            return;

        if (pMovement.CurrentStamina > 0 && localCombatState == CombatState.Idle)
        {
            StartNormalAttack();
        }
        else if (!isBufferingNextAttack)
        {
            isHoldingForNextAttack = true;
            isBufferingNextAttack = true;
        }
    }

    private void OnReleaseM1()
    {
        if (!IsOwner || pRagdoll.IsRagdolled) return;
        if (currentWeapon == null) return;

        if (localCombatState == CombatState.Blocking || localCombatState == CombatState.UnblockQueue)
            return;

        if (isBufferingNextAttack)
        {
            CancelBufferedAttack();
            return;
        }

        if (pMovement.CurrentStamina > 0 && (localCombatState == CombatState.Holding || localCombatState == CombatState.PreHeavyAttack))
        {
            if (viewModel != null)
                viewModel.SetCharge(false, direction);

            if (localCombatState == CombatState.Holding)
            {
                localCombatState = CombatState.LightAttack;
            }
            else if (localCombatState == CombatState.PreHeavyAttack)
            {
                localCombatState = CombatState.HeavyAttack;
            }
            else
            {
                return;
            }

            HandleWeaponAttack();
            RPC_Attack();
        }
    }

    private void OnStartM2()
    {
        if (!IsOwner || pRagdoll.IsRagdolled) return;
        if (currentWeapon == null) return;

        m2Held = true;

        if (localCombatState != CombatState.Idle &&
            localCombatState != CombatState.LightAttack &&
            localCombatState != CombatState.PreHeavyAttack)
        {
            blockBuffered = true;
            return;
        }

        if (localCombatState == CombatState.UnblockQueue)
            return;

        if (Time.time < lastLocalBlockReleaseTime + currentWeapon.defense.blockCooldown)
            return;

        if (localCombatState == CombatState.Blocking)
            return;

        LocalCancelAttackPresentation();

        blockBuffered = false;
        localCombatState = CombatState.Blocking;
        RPC_Block();
        if (viewModel != null)
            viewModel.SetBlock(true);
    }

    private void OnReleaseM2()
    {
        if (!IsOwner) return;
        if (currentWeapon == null) return;

        m2Held = false;
        blockBuffered = false;

        if (localCombatState != CombatState.Blocking)
            return;

        localCombatState = CombatState.UnblockQueue;
        lastLocalBlockReleaseTime = Time.time;
        RPC_Unblock();
    }
    #endregion

    private void StartNormalAttack()
    {
        localCombatState = CombatState.Holding;
        startHoldingTime = Time.time;
        direction = direction * -1;
        if (chargeRoutine != null) StopCoroutine(chargeRoutine);
        chargeRoutine = StartCoroutine(ChargeAttack(startHoldingTime));
        RPC_StartAttack(direction);
    }

    private IEnumerator ChargeAttack(float startHoldingTime)
    {
        if (!IsOwner) yield break;
        yield return new WaitForSeconds(0.2f);
        if (this.startHoldingTime == startHoldingTime && localCombatState == CombatState.Holding)
        {
            localCombatState = CombatState.PreHeavyAttack;
            if (viewModel != null)
                viewModel.SetCharge(true, direction);
        }
    }

    private void CancelBufferedAttack()
    {
        isBufferingNextAttack = false;
        isHoldingForNextAttack = false;
        Debug.Log("Buffered attack cancelled");
    }

    private void LocalCancelAttackPresentation()
    {
        startHoldingTime = -999f;

        if (chargeRoutine != null)
        {
            StopCoroutine(chargeRoutine);
            chargeRoutine = null;
        }

        if (localHitboxRoutine != null)
        {
            StopCoroutine(localHitboxRoutine);
            localHitboxRoutine = null;
        }

        if (viewModel != null)
        {
            viewModel.SetCharge(false, direction);
        }
    }

    private void TryConsumeBufferedBlock()
    {
        if (!IsOwner) return;
        if (currentWeapon == null) return;

        if (localCombatState != CombatState.Idle &&
            localCombatState != CombatState.LightAttack &&
            localCombatState != CombatState.PreHeavyAttack)
            return;

        if (localCombatState == CombatState.UnblockQueue)
            return;

        if (Time.time < lastLocalBlockReleaseTime + currentWeapon.defense.blockCooldown)
            return;

        if (localCombatState == CombatState.Blocking)
        {
            blockBuffered = false;
            return;
        }

        LocalCancelAttackPresentation();
        blockBuffered = false;
        localCombatState = CombatState.Blocking;
        RPC_Block();
        if (viewModel != null)
            viewModel.SetBlock(true);
    }

    private void HandleWeaponAttack()
    {
        switch (localCombatState)
        {
            case CombatState.LightAttack:
                LightAttack();
                break;
            case CombatState.HeavyAttack:
                HeavyAttack();
                break;
            default:
                Debug.LogWarning($"Unexpected combat state: {localCombatState}");
                return;
        }
    }

    private void LightAttack()
    {
        AttackData attackData = currentWeapon.lightAttack;
        Vector3 viewpunchDir = attackData.viewPunch * direction;
        viewpunchDir.x = attackData.viewPunch.x;

        pCamera.AddViewPunch(viewpunchDir);
        if (viewModel != null)
            viewModel.LightAttack(direction);
        Transform hitbox = direction == 1 ? leftHandHitbox : rightHandHitbox;
        if (localHitboxRoutine != null) StopCoroutine(localHitboxRoutine);
        localHitboxRoutine = StartCoroutine(StartHitbox(hitbox, null, attackData));
    }

    private void HeavyAttack()
    {
        AttackData attackData = currentWeapon.heavyAttack;
        Vector3 viewpunchDir = attackData.viewPunch * direction;
        viewpunchDir.x = attackData.viewPunch.x;

        pCamera.AddViewPunch(viewpunchDir);
        if (viewModel != null)
            viewModel.HeavyAttack(direction);
        Transform hitbox = direction == 1 ? leftHandHitbox : rightHandHitbox;
        if (localHitboxRoutine != null) StopCoroutine(localHitboxRoutine);
        localHitboxRoutine = StartCoroutine(StartHitbox(hitbox, null, attackData));
    }

    private IEnumerator StartHitbox(Transform baseTransform, Transform tipTransform, AttackData attack)
    {
        if (!IsOwner) yield break;
        if (attack == null || baseTransform == null)
            yield break;

        float startTime = Time.time;
        float duration = Mathf.Max(0.01f, attack.duration);
        float endTime = startTime + duration;

        float windowStartSec = Mathf.Clamp(attack.hitWindowStart, 0f, duration);
        float windowEndSec = Mathf.Clamp(attack.hitWindowEnd, 0f, duration);
        if (windowEndSec < windowStartSec)
        {
            float tmp = windowStartSec;
            windowStartSec = windowEndSec;
            windowEndSec = tmp;
        }

        float windowStartTime = startTime + windowStartSec;
        float windowEndTime = startTime + windowEndSec;

        HashSet<int> hitDedup = new HashSet<int>(16);
        RaycastHit[] castHits = new RaycastHit[32];
        Collider[] overlapHits = new Collider[32];

        Vector3 prevBase = GetHitBaseWorld(baseTransform, attack);
        Vector3 prevTip = GetHitTipWorld(baseTransform, tipTransform, attack);
        ClampHitPointsOutOfOcclusion(baseTransform, attack, ref prevBase, ref prevTip);

        while (Time.time < endTime)
        {
            float now = Time.time;

            Vector3 currBase = GetHitBaseWorld(baseTransform, attack);
            Vector3 currTip = GetHitTipWorld(baseTransform, tipTransform, attack);
            ClampHitPointsOutOfOcclusion(baseTransform, attack, ref currBase, ref currTip);

            if (now >= windowStartTime && now <= windowEndTime)
            {
                bool sentAnyHitThisFrame = false;
                Vector3 occlusionOrigin = attack.hitShape == AttackData.HitShape.Sphere
                    ? currBase
                    : (currBase + currTip) * 0.5f;

                if (attack.hitShape == AttackData.HitShape.Sphere)
                {
                    bool hitA = DoSphereSweep(prevBase, currBase, attack.hitRadius, hitMask, castHits, hitDedup, occlusionOrigin, baseTransform);
                    bool hitB = !hitA && DoSphereOverlap(currBase, attack.hitRadius, hitMask, overlapHits, hitDedup, baseTransform, occlusionOrigin);
                    sentAnyHitThisFrame = (hitA || hitB);
                }
                else
                {
                    bool hitA = DoCapsuleSweep(prevBase, prevTip, currBase, currTip, attack.hitRadius, hitMask, castHits, hitDedup, occlusionOrigin, baseTransform);
                    bool hitB = !hitA && DoCapsuleOverlap(currBase, currTip, attack.hitRadius, hitMask, overlapHits, hitDedup, baseTransform, occlusionOrigin);
                    sentAnyHitThisFrame = (hitA || hitB);
                }

                if (sentAnyHitThisFrame)
                    yield break;
            }

            prevBase = currBase;
            prevTip = currTip;
            yield return null;
        }
    }

    private Vector3 GetHitBaseWorld(Transform baseTransform, AttackData attack)
    {
        Vector3 pos = baseTransform.position;
        if (attack != null && Mathf.Abs(attack.extraForwardOffset) > 0.0001f)
            pos += baseTransform.forward * attack.extraForwardOffset;
        return pos;
    }

    private Vector3 GetHitTipWorld(Transform baseTransform, Transform tipTransform, AttackData attack)
    {
        if (tipTransform != null)
            return tipTransform.position;

        float len = attack != null ? Mathf.Max(0f, attack.hitLength) : 0f;
        return GetHitBaseWorld(baseTransform, attack) + baseTransform.forward * len;
    }

    private bool DoSphereSweep(
        Vector3 prevCenter,
        Vector3 currCenter,
        float radius,
        LayerMask mask,
        RaycastHit[] hits,
        HashSet<int> dedup,
        Vector3 occlusionOrigin,
        Transform fallbackForward)
    {
        Vector3 delta = currCenter - prevCenter;
        float dist = delta.magnitude;
        if (dist > 0.0001f)
        {
            Vector3 dir = delta / dist;
            int count = Physics.SphereCastNonAlloc(prevCenter, radius, dir, hits, dist, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (TrySendHit(hits[i], dedup, occlusionOrigin, fallbackForward))
                    return true;
        }
        return false;
    }

    private bool DoCapsuleSweep(
        Vector3 prevBase,
        Vector3 prevTip,
        Vector3 currBase,
        Vector3 currTip,
        float radius,
        LayerMask mask,
        RaycastHit[] hits,
        HashSet<int> dedup,
        Vector3 occlusionOrigin,
        Transform fallbackForward)
    {
        Vector3 prevCenter = (prevBase + prevTip) * 0.5f;
        Vector3 currCenter = (currBase + currTip) * 0.5f;
        Vector3 delta = currCenter - prevCenter;
        float dist = delta.magnitude;
        if (dist <= 0.0001f) return false;

        Vector3 dir = delta / dist;

        int countA = Physics.CapsuleCastNonAlloc(prevBase, prevTip, radius, dir, hits, dist, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < countA; i++)
            if (TrySendHit(hits[i], dedup, occlusionOrigin, fallbackForward))
                return true;

        int countB = Physics.CapsuleCastNonAlloc(currBase, currTip, radius, -dir, hits, dist, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < countB; i++)
            if (TrySendHit(hits[i], dedup, occlusionOrigin, fallbackForward))
                return true;

        return false;
    }

    private bool DoSphereOverlap(
        Vector3 center,
        float radius,
        LayerMask mask,
        Collider[] overlaps,
        HashSet<int> dedup,
        Transform fallbackForward,
        Vector3 occlusionOrigin)
    {
        int count = Physics.OverlapSphereNonAlloc(center, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlaps[i];
            if (c == null) continue;

            if (!HitboxPhysics.TryGetOverlapSurfaceHit(c, center, occlusionOrigin, fallbackForward, out Vector3 hitPoint, out Vector3 hitNormal))
                continue;

            if (TrySendHit(c.gameObject, c, hitPoint, hitNormal, dedup, occlusionOrigin, fallbackForward))
                return true;
        }
        return false;
    }

    private bool DoCapsuleOverlap(
        Vector3 basePos,
        Vector3 tipPos,
        float radius,
        LayerMask mask,
        Collider[] overlaps,
        HashSet<int> dedup,
        Transform fallbackForward,
        Vector3 occlusionOrigin)
    {
        int count = Physics.OverlapCapsuleNonAlloc(basePos, tipPos, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
        Vector3 center = (basePos + tipPos) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Collider c = overlaps[i];
            if (c == null) continue;

            if (!HitboxPhysics.TryGetOverlapSurfaceHit(c, center, occlusionOrigin, fallbackForward, out Vector3 hitPoint, out Vector3 hitNormal))
                continue;

            if (TrySendHit(c.gameObject, c, hitPoint, hitNormal, dedup, occlusionOrigin, fallbackForward))
                return true;
        }
        return false;
    }

    private void ClampHitPointsOutOfOcclusion(Transform baseTransform, AttackData attack, ref Vector3 basePoint, ref Vector3 tipPoint)
    {
        if (attack == null || occlusionMask.value == 0) return;

        Vector3 fwd = baseTransform != null ? baseTransform.forward : transform.forward;
        float checkRadius = Mathf.Clamp(attack.hitRadius * 0.35f, 0.02f, 0.12f);
        const float step = 0.06f;
        const int maxSteps = 8;

        basePoint = ClampPointOutOfOcclusion(basePoint, -fwd, checkRadius, maxSteps, step);
        tipPoint = ClampPointOutOfOcclusion(tipPoint, -fwd, checkRadius, maxSteps, step);
    }

    private Vector3 ClampPointOutOfOcclusion(Vector3 p, Vector3 pushDir, float checkRadius, int maxSteps, float step)
    {
        if (pushDir.sqrMagnitude <= 0.0001f) return p;
        pushDir.Normalize();

        for (int i = 0; i < maxSteps; i++)
        {
            if (!Physics.CheckSphere(p, checkRadius, occlusionMask, QueryTriggerInteraction.Ignore))
                break;
            p += pushDir * step;
        }
        return p;
    }

    private bool TrySendHit(RaycastHit hit, HashSet<int> dedup, Vector3 occlusionOrigin, Transform fallbackForward)
    {
        if (hit.collider == null) return false;

        Vector3 correctedPoint = hit.point;
        Vector3 correctedNormal = hit.normal;

        HitboxPhysics.TryGetOverlapSurfaceHit(hit.collider, occlusionOrigin, occlusionOrigin, fallbackForward, out correctedPoint, out correctedNormal);

        return TrySendHit(hit.collider.gameObject, hit.collider, correctedPoint, correctedNormal, dedup, occlusionOrigin, fallbackForward);
    }

    private bool TrySendHit(
        GameObject hitObject,
        Collider hitCollider,
        Vector3 hitPoint,
        Vector3 hitNormal,
        HashSet<int> dedup,
        Vector3 occlusionOrigin,
        Transform fallbackForward)
    {
        Vector3 origin = occlusionOrigin;
        if (origin == default && fallbackForward != null)
            origin = fallbackForward.position;

        if (!HitboxPhysics.ResolveImpact(this, origin, hitCollider, hitPoint, hitNormal, occlusionMask.value != 0 ? occlusionMask : hitMask, out HitboxPhysics.Impact impact))
            return false;

        if (!impact.IsWorld)
            if (!dedup.Add(impact.TargetObjectId))
                return false;

        PreciseTick pt = TimeManager != null ? TimeManager.GetPreciseTick(TickType.LocalTick) : default;
        RPC_SendHitReport(impact.TargetObjectId, impact.Point, impact.Normal, pt);
        return true;
    }

    [TargetRpc]
    private void TargetRPC_SetCombatState(NetworkConnection conn, CombatState state)
    {
        localCombatState = state;
        if (localCombatState == CombatState.Idle && viewModel != null)
            viewModel.SetBlock(false);
    }

    [TargetRpc]
    private void TargetRPC_CheckBufferedAttack(NetworkConnection conn)
    {
        if (isBufferingNextAttack && isHoldingForNextAttack)
        {
            isBufferingNextAttack = false;
            isHoldingForNextAttack = false;

            if (pMovement.CurrentStamina > 0 && localCombatState == CombatState.Idle)
            {
                StartNormalAttack();
            }
        }
        else if (isBufferingNextAttack)
        {
            isBufferingNextAttack = false;
            isHoldingForNextAttack = false;
        }
    }

    [TargetRpc]
    private void TargetRPC_QuickState(NetworkConnection conn, string state, Color color, float duration) => pUI.QuickState(state, color, duration);

    [TargetRpc]
    private void TargetRPC_OnOwnAttackParried(NetworkConnection conn, int otherPlayerObjectId, Vector3 hitPoint)
    {
        //viewModel.Parry();
    }

    [TargetRpc]
    private void TargetRPC_OnOwnBlockHit(NetworkConnection conn, int otherPlayerObjectId, Vector3 hitPoint, bool wasHeavy)
    {
        viewModel.BlockHit();
    }

    [Server]
    public void ServerNotifyOwnAttackParried(int otherPlayerObjectId, Vector3 hitPoint)
    {
        if (CanSendToOwner())
            TargetRPC_OnOwnAttackParried(base.Owner, otherPlayerObjectId, hitPoint);
    }

    [TargetRpc]
    private void TargetRPC_OnOwnParrySuccess(NetworkConnection conn, int otherPlayerObjectId, Vector3 hitPoint)
    {
        m2Held = false;
        blockBuffered = false;

        viewModel.Parry();
        pUI.QuickState("parried", Color.green, 0.5f);
    }

    [Server]
    public void ServerNotifyOwnParrySuccess(int otherPlayerObjectId, Vector3 hitPoint)
    {
        if (pAnimations != null)
            pAnimations.Parry();

        if (CanSendToOwner())
            TargetRPC_OnOwnParrySuccess(base.Owner, otherPlayerObjectId, hitPoint);
    }

    [Server]
    public void ServerNotifyOwnBlockHit(int otherPlayerObjectId, Vector3 hitPoint, bool wasHeavy)
    {
        if (pAnimations != null)
            pAnimations.BlockHit();

        if (pMovement != null)
            pMovement.ConsumeStamina(currentWeapon.defense.blockStamina);

        if (CanSendToOwner())
            TargetRPC_OnOwnBlockHit(base.Owner, otherPlayerObjectId, hitPoint, wasHeavy);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RPC_StartAttack(int dir)
    {
        Server_DoStartAttack(dir);
    }

    [Server]
    public void Server_DoStartAttack(int dir)
    {
        if (isAttacking) return;
        if (pRagdoll.IsRagdolled) return;
        serverAttackDirection = dir == 0 ? 1 : (dir > 0 ? 1 : -1);
        if (pMovement.CurrentStamina > 0)
        {
            attackHoldStartTick = TimeManager.Tick;
            isAttackReady = true;

            if (serverChargeRoutine != null) StopCoroutine(serverChargeRoutine);
            serverChargeRoutine = StartCoroutine(ServerChargeRoutine(attackHoldStartTick, serverAttackDirection));
        }
    }

    private IEnumerator ServerChargeRoutine(uint holdStartTick, int dir)
    {
        if (!IsServerInitialized) yield break;
        yield return new WaitForSeconds(0.2f);

        if (!isAttackReady || isAttacking) yield break;
        if (attackHoldStartTick != holdStartTick) yield break;
        if (isBlocking || unblockQueue) yield break;

        if (pAnimations != null)
            pAnimations.SetCharge(true, dir);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RPC_Attack()
    {
        Server_DoAttack();
    }

    [Server]
    public void Server_DoAttack()
    {
        if (isAttacking)
        {
            if (CanSendToOwner())
                TargetRPC_QuickState(base.Owner, "[Reported] Attack issue. Server already attacking.", Color.red, 1.5f);
            return;
        }

        if (isBlocking || unblockQueue)
        {
            if (CanSendToOwner())
                TargetRPC_SetCombatState(base.Owner, unblockQueue ? CombatState.UnblockQueue : CombatState.Blocking);
            return;
        }

        if (pRagdoll.IsRagdolled) return;

        if (!isAttackReady)
        {
            if (CanSendToOwner())
            {
                TargetRPC_SetCombatState(base.Owner, CombatState.Idle);
                TargetRPC_QuickState(base.Owner, "[Reported] Attack issue. Server not ready for attack.", Color.red, 1.5f);
            }
            
            return;
        }

        uint nowTick = TimeManager.Tick;
        uint holdTicks = nowTick - attackHoldStartTick;
        uint minHeavyTicks = TimeManager.TimeToTicks(0.2f, TickRounding.RoundUp);
        bool isHeavy = holdTicks > minHeavyTicks;

        currentAttackData = isHeavy ? currentWeapon.heavyAttack : currentWeapon.lightAttack;

        uint attackTicks = TimeManager.TimeToTicks(currentAttackData.duration, TickRounding.RoundUp);

        if (pMovement.CurrentStamina > 0)
        {
            isAttacking = true;
            isAttackReady = false;
            serverRegisteredDamageHitThisAttack = false;
            attackExecuteTick = nowTick;
            attackEndTick = nowTick + attackTicks;

            if (CanSendToOwner())
            {
                pMovement.ConsumeStamina(currentAttackData.staminaCost, currentAttackData.staminaRegenDelay);
                TargetRPC_QuickState(base.Owner, currentAttackData.name, Color.red, 0.5f);
            }

            if (pAnimations != null)
            {
                pAnimations.SetCharge(false, serverAttackDirection);
                if (isHeavy) pAnimations.HeavyAttack(serverAttackDirection);
                else pAnimations.LightAttack(serverAttackDirection);
            }
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void RPC_SendHitReport(int targetObjectId, Vector3 hitPoint, Vector3 hitNormal, PreciseTick preciseTick)
    {
        Server_DoSendHitReport(targetObjectId, hitPoint, hitNormal, preciseTick);
    }

    [Server]
    public void Server_DoSendHitReport(int targetObjectId, Vector3 hitPoint, Vector3 hitNormal, PreciseTick preciseTick)
    {
        if (!isAttacking)
        {
            if (CanSendToOwner())
                TargetRPC_QuickState(base.Owner, "[Reported] Hit issue. Server not attacking.", Color.red, 1.5f);
            return;
        }

        if (pRagdoll.IsRagdolled) return;
        if (serverRegisteredDamageHitThisAttack) return;
        if (currentAttackData == null || TimeManager == null) return;

        if (!IsHitReportValid(targetObjectId, hitPoint, preciseTick, out string rejectReason))
        {
            if (CanSendToOwner())
                TargetRPC_QuickState(base.Owner, $"[Reported] Invalid hit: {rejectReason}", Color.red, 1.5f);
            return;
        }

        Vector3 origin = transform.position;
        if (currentAttackData != null && Mathf.Abs(currentAttackData.extraForwardOffset) > 0.0001f)
            origin += transform.forward * currentAttackData.extraForwardOffset;

        if (targetObjectId == -1)
        {
            if (occlusionMask.value != 0)
            {
                Vector3 dir = hitPoint - origin;
                float dist = dir.magnitude;
                if (dist > 0.0001f)
                {
                    dir /= dist;
                    if (Physics.Raycast(origin, dir, out RaycastHit rh, Mathf.Max(0f, dist - 0.01f), occlusionMask, QueryTriggerInteraction.Ignore))
                    {
                        hitPoint = rh.point;
                        hitNormal = rh.normal;
                    }
                }
            }

            NetworkObject nob = NetworkManager.GetPooledInstantiated(concreteImpactFX, hitPoint, Quaternion.LookRotation(hitNormal), true);
            ServerManager.Spawn(nob);
            return;
        }

        if (!base.NetworkManager.ServerManager.Objects.Spawned.TryGetValue(targetObjectId, out NetworkObject targetNob) || targetNob == null)
        {
            if (CanSendToOwner())
                TargetRPC_QuickState(base.Owner, "[Reported] Invalid hit: target not spawned.", Color.red, 1.5f);
            return;
        }

        if (targetNob == base.NetworkObject)
        {
            if (CanSendToOwner())
                TargetRPC_QuickState(base.Owner, "[Reported] Invalid hit: self target.", Color.red, 1.5f);
            return;
        }

        Collider targetCollider = GetBestTargetCollider(targetNob, hitPoint);
        if (targetCollider == null)
        {
            if (CanSendToOwner())
                TargetRPC_QuickState(base.Owner, "[Reported] Invalid hit: no collider.", Color.red, 1.5f);
            return;
        }

        if (HitboxPhysics.ResolveImpact(this, origin, targetCollider, hitPoint, hitNormal, occlusionMask.value != 0 ? occlusionMask : hitMask, out HitboxPhysics.Impact impact))
        {
            if (impact.IsWorld) return;
            hitPoint = impact.Point;
            hitNormal = impact.Normal;
        }

        PlayerHealth health = targetNob.GetComponent<PlayerHealth>();
        if (health == null)
        {
            if (CanSendToOwner())
                TargetRPC_QuickState(base.Owner, "[Reported] Invalid hit: target has no health.", Color.red, 1.5f);
            return;
        }

        float damageMultiplier = 1f;
        bool isBackstab = false;
        bool wasBlocked = false;
        bool wasParried = false;

        PlayerContext victim = PlayerContext.Get(targetNob);
        PlayerCombat victimCombat = victim != null ? victim.Combat : null;

        if (victimCombat != null && victimCombat.currentWeapon != null && victimCombat.currentWeapon.defense != null)
        {
            DefenseData def = victimCombat.currentWeapon.defense;

            Vector3 attackerPos = transform.position;
            Vector3 victimPos = targetNob.transform.position;
            Vector3 dirVictimToAttacker = (attackerPos - victimPos);
            if (dirVictimToAttacker.sqrMagnitude > 0.0001f)
                dirVictimToAttacker.Normalize();
            else
                dirVictimToAttacker = targetNob.transform.forward;

            float dotFront = Vector3.Dot(targetNob.transform.forward, dirVictimToAttacker);
            bool attackerInFront = dotFront >= def.frontDotThreshold;
            bool attackerBehind = dotFront <= def.backstabDotThreshold;

            isBackstab = attackerBehind;

            if (victimCombat.TryGetServerBlockInfo(out uint victimBlockStartTick) && attackerInFront)
            {
                uint parryTicks = TimeManager.TimeToTicks(Mathf.Max(0f, def.parryWindow), TickRounding.RoundUp);
                wasParried = (parryTicks > 0u) && (TimeManager.Tick - victimBlockStartTick <= parryTicks);
                if (wasParried)
                {
                    damageMultiplier = 0f;
                }
                else
                {
                    wasBlocked = true;
                    if (currentAttackData == currentWeapon.lightAttack && victim.Movement.CurrentStamina > 0)
                        damageMultiplier = 0f;
                    else
                        damageMultiplier = Mathf.Clamp01(heavyBlockDamageMultiplier);
                }
            }
        }

        if (!wasParried && !wasBlocked && isBackstab)
            damageMultiplier *= Mathf.Max(0f, backstabDamageMultiplier);

        int attackerId = NetworkObject != null ? NetworkObject.ObjectId : -1;
        float hitboxMultiplier = 1f;
        HitboxGroup victimHitboxGroup = victim?.HitboxGroup;
        if (victimHitboxGroup != null)
        {
            hitboxMultiplier = victimHitboxGroup.GetDamageMultiplier(targetCollider);
        }

        health.TakeCombatDamage(currentAttackData, damageMultiplier, attackerId, isBackstab, TimeManager.Tick, hitboxMultiplier);

        if (damageMultiplier * hitboxMultiplier > 0)
        {
            NetworkObject nob = NetworkManager.GetPooledInstantiated(bloodImpactFX, hitPoint, Quaternion.LookRotation(hitNormal), true);
            ServerManager.Spawn(nob);
        }
        else
        {
            NetworkObject nob = NetworkManager.GetPooledInstantiated(concreteImpactFX, hitPoint, Quaternion.LookRotation(hitNormal), true);
            ServerManager.Spawn(nob);
        }

        if (wasParried)
        {
            ServerNotifyOwnAttackParried(targetNob.ObjectId, hitPoint);
            if (victimCombat != null)
            {
                victimCombat.ServerNotifyOwnParrySuccess(attackerId, hitPoint);
                victimCombat.Server_StopBlock();
            }
        }
        else if (wasBlocked && victimCombat != null)
        {
            bool wasHeavy = currentAttackData == currentWeapon.heavyAttack;
            victimCombat.ServerNotifyOwnBlockHit(attackerId, hitPoint, wasHeavy);
        }

        if (victimHitboxGroup != null && damageMultiplier * hitboxMultiplier > 0)
        {
            Vector3 attackerToTarget = targetNob.transform.position - transform.position;

            attackerToTarget.y = 0f;

            if (attackerToTarget.sqrMagnitude < 0.01f)
                attackerToTarget = transform.forward;

            Vector3 hitDirection = attackerToTarget.normalized;

            float rawDamage = currentAttackData.baseDamage * damageMultiplier * hitboxMultiplier;
            victimHitboxGroup.ServerTriggerHitReaction(targetCollider, hitDirection, hitPoint, rawDamage);
        }

        serverRegisteredDamageHitThisAttack = true;

        if (CanSendToOwner())
            TargetRPC_QuickState(base.Owner, "Player hit.", Color.green, 1.5f);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RPC_Block()
    {
        Server_DoBlock();
    }

    [Server]
    public void Server_DoBlock()
    {
        if (isBlocking) return;
        if (unblockQueue) return;
        if (pRagdoll.IsRagdolled) return;

        bool duringChargePrep = isAttackReady && !isAttacking;
        bool duringLightAttack = isAttacking && currentAttackData == currentWeapon.lightAttack;
        bool duringIdle = !isAttacking && !isAttackReady;
        if (!duringIdle && !duringChargePrep && !duringLightAttack)
        {
            return;
        }

        isBlocking = true;
        unblockQueue = false;
        blockStartTick = TimeManager.Tick;

        if (isAttacking || isAttackReady)
            Server_StopAttackImmediate();

        if (pAnimations != null)
            pAnimations.SetBlock(true);

        if (CanSendToOwner())
            TargetRPC_SetCombatState(base.Owner, CombatState.Blocking);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RPC_Unblock()
    {
        if (!isBlocking) return;
        unblockQueue = true;

        if (CanSendToOwner())
            TargetRPC_SetCombatState(base.Owner, CombatState.UnblockQueue);
    }

    [Server]
    private bool IsHitReportValid(int targetObjectId, Vector3 hitPoint, PreciseTick preciseTick, out string reason)
    {
        reason = string.Empty;

        uint startTick = attackExecuteTick;
        uint endTick = attackEndTick;
        if (endTick <= startTick)
        {
            reason = "bad server window";
            return false;
        }

        uint winStartTicks = TimeManager.TimeToTicks(Mathf.Max(0f, currentAttackData.hitWindowStart), TickRounding.RoundUp);
        uint winEndTicks = TimeManager.TimeToTicks(Mathf.Max(0f, currentAttackData.hitWindowEnd), TickRounding.RoundUp);
        if (winEndTicks < winStartTicks)
        {
            uint tmp = winStartTicks;
            winStartTicks = winEndTicks;
            winEndTicks = tmp;
        }

        uint windowStart = startTick + winStartTicks;
        uint windowEnd = startTick + winEndTicks;

        uint slack = serverHitWindowSlackTicks;
        uint min = (windowStart > slack) ? (windowStart - slack) : 0u;
        uint max = windowEnd + slack;

        uint serverNow = TimeManager.Tick;
        if (serverNow < min || serverNow > max)
        {
            reason = "out of hit window";
            return false;
        }

        Vector3 origin = transform.position;
        if (currentAttackData != null && Mathf.Abs(currentAttackData.extraForwardOffset) > 0.0001f)
            origin += transform.forward * currentAttackData.extraForwardOffset;

        Vector3 effectivePoint = hitPoint;

        if (targetObjectId != -1 &&
            NetworkManager?.ServerManager?.Objects?.Spawned.TryGetValue(targetObjectId, out NetworkObject targetNob) == true &&
            targetNob != null)
        {
            Collider c = GetBestTargetCollider(targetNob, hitPoint);
            if (c != null)
                effectivePoint = c.ClosestPoint(hitPoint);
        }

        float reach = currentAttackData.hitRadius;
        if (currentAttackData.hitShape == AttackData.HitShape.Capsule)
            reach += Mathf.Max(0f, currentAttackData.hitLength);
        reach += Mathf.Max(0f, currentAttackData.extraForwardOffset);
        float maxDist = Mathf.Max(0f, serverBaseReach) + reach + serverHitRangeSlack;

        if ((effectivePoint - origin).sqrMagnitude > (maxDist * maxDist))
        {
            reason = "too far";
            return false;
        }

        return true;
    }

    private static Collider GetBestTargetCollider(NetworkObject targetNob, Vector3 nearPoint)
    {
        if (targetNob == null) return null;

        HitboxGroup hitboxGroup = PlayerContext.Get(targetNob).HitboxGroup;
        if (hitboxGroup != null && hitboxGroup.TryGetClosestHitbox(nearPoint, out HitboxGroup.HitboxEntry entry))
        {
            return entry.collider;
        }

        Collider[] cols = targetNob.GetComponentsInChildren<Collider>();
        if (cols == null || cols.Length == 0) return null;

        Collider best = null;
        float bestSqr = float.PositiveInfinity;
        foreach (Collider c in cols)
        {
            if (c == null || !c.enabled || c.isTrigger) continue;

            Vector3 p = c.ClosestPoint(nearPoint);
            float sqr = (p - nearPoint).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = c;
            }
        }
        return best;
    }

    [Server]
    private void Server_StopAttack()
    {
        if (isAttacking && TimeManager.Tick >= attackEndTick)
        {
            isAttacking = false;
            attackHoldStartTick = 0;
            attackExecuteTick = 0;
            isAttackReady = false;
            currentAttackData = null;
            serverRegisteredDamageHitThisAttack = false;

            if (!isBlocking && !unblockQueue)
            {
                if (CanSendToOwner())
                    TargetRPC_SetCombatState(base.Owner, CombatState.Idle);
            }

            if (CanSendToOwner())
                TargetRPC_CheckBufferedAttack(base.Owner);

            if (pAnimations != null)
                pAnimations.SetCharge(false, serverAttackDirection);
        }
    }

    [Server]
    private void Server_StopAttackImmediate()
    {
        if (!isAttacking && !isAttackReady) return;

        isAttacking = false;
        isAttackReady = false;
        attackHoldStartTick = 0;
        attackExecuteTick = 0;
        attackEndTick = 0;
        currentAttackData = null;
        serverRegisteredDamageHitThisAttack = false;

        if (pAnimations != null)
            pAnimations.SetCharge(false, serverAttackDirection);
    }

    [Server]
    public void Server_StartSimpleHitbox()
    {
        if (currentAttackData == null) return;

        uint startTick = TimeManager.Tick;
        uint durationTicks = TimeManager.TimeToTicks(currentAttackData.duration, TickRounding.RoundUp);

        uint windowStart = startTick + TimeManager.TimeToTicks(currentAttackData.hitWindowStart, TickRounding.RoundUp);
        uint windowEnd = startTick + TimeManager.TimeToTicks(currentAttackData.hitWindowEnd, TickRounding.RoundUp);

        StartCoroutine(Server_HitboxTickRoutine(startTick, durationTicks, windowStart, windowEnd));
    }

    private IEnumerator Server_HitboxTickRoutine(uint startTick, uint durationTicks, uint windowStart, uint windowEnd)
    {
        HashSet<int> hitDedup = new HashSet<int>();

        while (TimeManager.Tick < startTick + durationTicks)
        {
            uint now = TimeManager.Tick;

            if (now >= windowStart && now <= windowEnd)
            {
                Collider[] hits = Physics.OverlapSphere(
                    cameraPosition.position + cameraPosition.forward,
                    currentAttackData.hitRadius,
                    hitMask
                );

                foreach (var c in hits)
                {
                    NetworkObject nob = c.GetComponentInParent<NetworkObject>();
                    if (nob == null || nob == base.NetworkObject) continue;

                    if (!hitDedup.Add(nob.ObjectId)) continue;

                    Vector3 point = c.ClosestPoint(cameraPosition.position);
                    Vector3 normal = (point - cameraPosition.position).normalized;

                    PreciseTick pt = TimeManager.GetPreciseTick(TickType.LocalTick);

                    Server_DoSendHitReport(nob.ObjectId, point, normal, pt);
                    yield break;
                }
            }

            yield return null;
        }
    }

    [Server]
    private void Server_StopBlock()
    {
        if (isBlocking)
        {
            isBlocking = false;
            unblockQueue = false;
            blockStartTick = 0;

            if (CanSendToOwner())
            {
                TargetRPC_QuickState(base.Owner, "Server Unblock", Color.red, 0.5f);
                TargetRPC_SetCombatState(base.Owner, CombatState.Idle);
            }
                
            if (pAnimations != null)
                pAnimations.SetBlock(false);
        }
    }
}