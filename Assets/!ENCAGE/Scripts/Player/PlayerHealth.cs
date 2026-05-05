using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Security.Cryptography;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    public readonly SyncVar<float> _health = new SyncVar<float>();

    [SerializeField]
    private float maxHealth = 100f;
    public float Health => _health.Value;

    private PlayerContext ctx;
    private PlayerUI pUI;
    private PlayerCamera pCamera;
    private PlayerRagdoll pRagdoll;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        ctx = PlayerContext.Get(NetworkObject);
        pUI = ctx.UI;
        pRagdoll = ctx.Ragdoll;
        pCamera = ctx.Camera;

        _health.OnChange += OnHealthChange;
        _health.Value = maxHealth;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _health.OnChange -= OnHealthChange;
    }

    [Server]
    public void TakeDamage(float amount)
    {
        _health.Value -= amount;
        OnDamage(Owner, amount);

        if (_health.Value <= 0f)
        {
            _health.Value = 0f;
            pRagdoll.ActivateRagdoll(Vector3.zero);
        }
    }

    [Server]
    public void TakeCombatDamage(AttackData attack, float damageMultiplier, int attackerObjectId, bool isBackstab, uint serverTick)
    {
        if (attack == null)
            return;

        float variance = Mathf.Clamp01(attack.damageVariance);
        float varianceMultiplier = 1f;

        if (variance > 0f)
        {
            varianceMultiplier = 1f + SecureSigned01(attackerObjectId, serverTick) * variance;
        }

        float amount = Mathf.Max(0f, attack.baseDamage * varianceMultiplier * Mathf.Max(0f, damageMultiplier));
        TakeDamage(amount);

        if (isBackstab)
        {
            pUI.TargetRPC_QuickState(Owner, "Backstabbed", Color.red, 0.5f);
        }
    }

    [Server]
    private static float SecureSigned01(int attackerObjectId, uint serverTick)
    {
        byte[] randomBytes = new byte[16];
        RandomNumberGenerator.Fill(randomBytes);

        randomBytes[0] ^= (byte)attackerObjectId;
        randomBytes[1] ^= (byte)(attackerObjectId >> 8);
        randomBytes[2] ^= (byte)serverTick;
        randomBytes[3] ^= (byte)(serverTick >> 8);

        uint combined = (uint)(randomBytes[0] | randomBytes[1] << 8 | randomBytes[2] << 16 | randomBytes[3] << 24);
        float normalized = combined / 4294967295f;

        return normalized * 2f - 1f;
    }

    [TargetRpc]
    private void OnDamage(NetworkConnection conn, float damageValue)
    {
        float hurtIntensity = (damageValue / maxHealth) * 500f;
        hurtIntensity = Mathf.Clamp(hurtIntensity, 0f, 500f);

        Vector3 hurtPunch = Random.insideUnitSphere * hurtIntensity;
        pCamera.AddViewPunch(hurtPunch);

        float strength = hurtIntensity / 250f;
        pCamera.TakeDamageFlash(strength);
    }

    private void OnHealthChange(float prev, float next, bool asServer)
    {
        if (IsOwner && !asServer && pUI != null)
        {
            pUI.UpdateHealth(prev, next);
        }
    }
}