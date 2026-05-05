using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

public class Dummy : NetworkBehaviour
{
    public enum DummyAction
    {
        LightAttack,
        HeavyAttack,
        AlwaysParry,
        AlwaysBlock
    }

    [SerializeField] private DummyAction dummyAction;
    private PlayerCombat pCombat;
    private uint nextActionTick;
    private uint pendingAttackTick;
    private int direction = 1;
    private bool attackQueued;

    public override void OnStartServer()
    {
        base.OnStartServer();
        pCombat = GetComponent<PlayerCombat>();
        TimeManager.OnTick += OnTick;
    }

    private void OnTick()
    {
        if (!IsServerInitialized || pCombat == null || pCombat.currentWeapon == null)
            return;

        switch (dummyAction)
        {
            case DummyAction.LightAttack:
                HandleLightAttack();
                break;

            case DummyAction.HeavyAttack:
                HandleHeavyAttack();
                break;
            case DummyAction.AlwaysBlock:
                HandleAlwaysBlock();
                break;
        }
    }

    private void HandleLightAttack()
    {
        uint currentTick = TimeManager.Tick;

        if (currentTick < nextActionTick)
            return;

        if (!attackQueued)
        {
            pCombat.Server_DoStartAttack(direction);
            pendingAttackTick = currentTick + 1;
            attackQueued = true;
            direction *= -1;

            return;
        }

        if (attackQueued && currentTick >= pendingAttackTick)
        {
            pCombat.Server_DoAttack();
            pCombat.Server_StartSimpleHitbox();

            uint attackTicks = TimeManager.TimeToTicks(
                pCombat.currentWeapon.lightAttack.duration,
                TickRounding.RoundUp
            );

            nextActionTick = currentTick + attackTicks;
            attackQueued = false;
        }
    }

    private void HandleHeavyAttack()
    {
        uint currentTick = TimeManager.Tick;

        if (currentTick < nextActionTick)
            return;

        if (!attackQueued)
        {
            pCombat.Server_DoStartAttack(direction);

            uint chargeTicks = TimeManager.TimeToTicks(
                1f,
                TickRounding.RoundUp
            );

            pendingAttackTick = currentTick + chargeTicks;
            attackQueued = true;
            direction *= -1;
            return;
        }

        if (attackQueued && currentTick >= pendingAttackTick)
        {
            pCombat.Server_DoAttack();
            pCombat.Server_StartSimpleHitbox();

            uint attackTicks = TimeManager.TimeToTicks(
                pCombat.currentWeapon.heavyAttack.duration,
                TickRounding.RoundUp
            );

            nextActionTick = currentTick + attackTicks;
            attackQueued = false;
        }
    }

    private void HandleAlwaysBlock()
    {
        if (!pCombat.ServerIsBlocking)
        {
            pCombat.Server_DoBlock();
        }
    }
}