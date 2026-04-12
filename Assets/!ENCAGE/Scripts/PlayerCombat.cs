using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public class PlayerCombat : NetworkBehaviour
{
    public enum CombatState
    {
        Idle,
        LightAttack
    }

    [Header("Attack Settings")]
    [SerializeField] private float lightAttackDuration = 0.5f;
    [SerializeField] private float lightAttackStaminaCost = 7.5f;

    [Header("Hit Window Settings")]
    [SerializeField] private Transform handHitboxPosition;
    [SerializeField] private float hitRadius = 0.5f;
    [SerializeField] private LayerMask hitMask;
    [SerializeField] private float hitWindowStart = 0.15f;
    [SerializeField] private float hitWindowEnd = 0.35f;

    private uint lightAttackTicks;
    private uint lastLocalAttackTick;

    private float attackTimer;
    private bool isAttackingLocal;
    private bool hasHitThisAttack;

    private PlayerInput pInput;
    private PlayerAnimations pAnimations;
    private PlayerMovement pMovement;
    private PlayerRagdoll pRagdoll;
    private WeaponViewModel weaponViewModel;

    private readonly SyncVar<uint> lastActionTick = new SyncVar<uint>(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner)
        {
            pInput = GetComponent<PlayerInput>();
            weaponViewModel = GetComponentInChildren<WeaponViewModel>();
            pInput.onLightAttack += HandleLightAttack;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (IsOwner && pInput != null)
            pInput.onLightAttack -= HandleLightAttack;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        pAnimations = GetComponent<PlayerAnimations>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        pMovement = GetComponent<PlayerMovement>();
        pRagdoll = GetComponent<PlayerRagdoll>();
        lightAttackTicks = TimeManager.TimeToTicks(lightAttackDuration, TickRounding.RoundUp);
    }

    private void Update()
    {
        if (IsOwner && isAttackingLocal)
        {
            attackTimer += Time.deltaTime;

            if (attackTimer >= hitWindowStart && attackTimer <= hitWindowEnd && !hasHitThisAttack)
            {
                CheckMeleeHit();
            }

            if (attackTimer > lightAttackDuration)
            {
                isAttackingLocal = false;
            }
        }
    }

    private void HandleLightAttack()
    {
        if (!IsOwner) return;

        uint currentLocalTick = TimeManager.LocalTick;
        uint safeCooldownTicks = lightAttackTicks;

        if ((currentLocalTick - lastLocalAttackTick) < safeCooldownTicks)
            return;

        if (!CanLightAttack(true)) return;

        weaponViewModel.LightAttack();
        lastLocalAttackTick = currentLocalTick;
        isAttackingLocal = true;
        attackTimer = 0f;
        hasHitThisAttack = false;

        RPC_LightAttack();
    }

    public bool CanLightAttack(bool isLocal)
    {
        if (pRagdoll.IsRagdolled) return false;

        if (pMovement.CurrentStamina < lightAttackStaminaCost)
            return false;

        if (!isLocal)
        {
            if ((TimeManager.Tick - lastActionTick.Value) < lightAttackTicks)
                return false;
        }

        return true;
    }

    private void CheckMeleeHit()
    {
        Collider[] hits = Physics.OverlapSphere(handHitboxPosition.position, hitRadius, hitMask);

        foreach (var hit in hits)
        {
            if (hit.transform.root == transform.root) continue;

            hasHitThisAttack = true;

            NetworkObject targetNetObj = hit.GetComponentInParent<NetworkObject>();
            Vector3 hitPoint = hit.ClosestPoint(handHitboxPosition.position);

            PreciseTick preciseTick = TimeManager.GetPreciseTick(TickType.LastPacketTick);

            RPC_SendHit(targetNetObj, hitPoint, preciseTick);

            break;
        }
    }

    [ServerRpc]
    private void RPC_LightAttack()
    {
        if (!CanLightAttack(false)) return;

        lastActionTick.Value = TimeManager.Tick;
        pAnimations.TriggerLightAttack();
        pMovement.ConsumeStamina(lightAttackStaminaCost, 1f);
    }

    [ServerRpc]
    private void RPC_SendHit(NetworkObject target, Vector3 hitPoint, PreciseTick pt)
    {
        if ((TimeManager.Tick - lastActionTick.Value) > lightAttackTicks + 10)
            return;

        bool isValidHit = false;

        // ↓ MONEY ISSUE ↓
        // RollbackManager.Rollback(pt, RollbackManager.PhysicsType.Physics);

        if (target != null)
        {
            float distance = Vector3.Distance(transform.position, target.transform.position);
            float maxValidDistance = 3.0f;

            if (distance <= maxValidDistance)
            {
                isValidHit = true;
                target.GetComponent<PlayerHealth>().TakeDamage(10);
            }
        }
        else
        {
            isValidHit = true;
        }

        // ↓ MONEY ISSUE ↓
        // RollbackManager.Return();

        if (isValidHit)
        {
            //Play particle
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (handHitboxPosition != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(handHitboxPosition.position, hitRadius);
        }
    }
}