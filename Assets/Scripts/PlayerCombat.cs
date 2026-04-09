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

    [SerializeField] private float lightAttackDuration = 0.5f;
    [SerializeField] private float lightAttackStaminaCost = 7.5f;

    private uint lightAttackTicks;
    private uint lastLocalAttackTick;
    private uint lastPredictedAttackLocalTick;

    private PlayerInput pInput;
    private PlayerAnimations pAnimations;
    private PlayerMovement pMovement;
    private PlayerRagdoll pRagdoll;
    private WeaponViewModel weaponViewModel;

    private readonly SyncVar<CombatState> currentCombatState = new SyncVar<CombatState>(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

    private readonly SyncVar<uint> lastActionTick = new SyncVar<uint>(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

    private float predictedStaminaCost;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner)
        {
            pInput = GetComponent<PlayerInput>();
            weaponViewModel = GetComponentInChildren<WeaponViewModel>();
            pInput.onLightAttack += HandleLightAttack;
            pMovement.onReconcile += ResetPredictedStaminaCost;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (IsOwner)
        {
            pInput.onLightAttack -= HandleLightAttack;
            pMovement.onReconcile -= ResetPredictedStaminaCost;
        }
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

    private void HandleLightAttack()
    {
        if (!IsOwner) return;

        uint currentLocalTick = TimeManager.LocalTick;
        uint safeCooldownTicks = lightAttackTicks + 2;

        if ((currentLocalTick - lastLocalAttackTick) < safeCooldownTicks)
            return;

        if (!CanLightAttack()) return;

        weaponViewModel.LightAttack();

        lastLocalAttackTick = currentLocalTick;
        lastPredictedAttackLocalTick = currentLocalTick;
        predictedStaminaCost = lightAttackStaminaCost;

        RPC_LightAttack();
    }

    public bool CanLightAttack()
    {
        uint localLastActionTick = TimeManager.TickToLocalTick(lastActionTick.Value);
        uint safeCooldownTicks = lightAttackTicks + 2;

        if ((TimeManager.LocalTick - localLastActionTick) < safeCooldownTicks)
            return false;

        bool hasUnconfirmedAttack = lastPredictedAttackLocalTick > localLastActionTick;
        float staminaDebt = hasUnconfirmedAttack ? lightAttackStaminaCost : 0f;

        float effectiveStamina = pMovement.CurrentStamina - staminaDebt;

        if (effectiveStamina < 1f)
            return false;

        if (pRagdoll.IsRagdolled)
            return false;

        return true;
    }

    [ServerRpc]
    private void RPC_LightAttack()
    {
        if (!CanLightAttack()) return;

        lastActionTick.Value = TimeManager.Tick;
        pAnimations.TriggerLightAttack();
        pMovement.ConsumeStamina(lightAttackStaminaCost, 0.15f);
    }

    public void ResetPredictedStaminaCost()
    {
        uint localLastActionTick = TimeManager.TickToLocalTick(lastActionTick.Value);

        if (lastPredictedAttackLocalTick <= localLastActionTick)
        {
            predictedStaminaCost = 0f;
            lastPredictedAttackLocalTick = 0;
        }
    }
}