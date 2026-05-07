using Drawing;
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
    private PlayerHealth pHealth;
    private PlayerMovement pMovement;
    private uint nextActionTick;
    private uint pendingAttackTick;
    private int direction = 1;
    private bool attackQueued;

    [Header("Debug")]
    [SerializeField] private float size = 0.2f;
    [SerializeField] private Vector3 textPivot;
    [SerializeField] private Quaternion rot;

    public override void OnStartServer()
    {
        base.OnStartServer();
        TimeManager.OnTick += OnTick;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        pCombat = GetComponent<PlayerCombat>();
        pHealth = GetComponent<PlayerHealth>();
        pMovement = GetComponent<PlayerMovement>();
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
            case DummyAction.AlwaysParry:
                HandleAlwaysParry();
                break;
        }
    }

    private void Update()
    {
        if(IsClientInitialized)
            Draw.ingame.Label3D(transform.position + textPivot, rot, $"{pHealth.Health}HP\nStunned: {pMovement.IsStunned(TimeManager.Tick)}\n{dummyAction}", size);
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

    private void HandleAlwaysParry()
    {
        if (!pCombat.AlwaysParry)
        {
            pCombat.AlwaysParry = true;
        }

        if (!pCombat.ServerIsBlocking)
        {
            pCombat.Server_DoBlock();
        }
    }
}