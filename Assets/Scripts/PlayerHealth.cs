using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    private readonly SyncVar<float> _health = new SyncVar<float>();
    [SerializeField]
    private float maxHealth = 100f;
    private PlayerUI pUI;
    private PlayerRagdoll pRagdoll;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        pUI = GetComponent<PlayerUI>();
        pRagdoll = GetComponent<PlayerRagdoll>();
        _health.OnChange += OnHealthChange;
        _health.Value = maxHealth;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _health.OnChange -= OnHealthChange;
    }

    public float GetHealth()
    {
        return _health.Value;
    }
    [Server]
    public void TakeDamage(float amount)
    {
        _health.Value -= amount;
        if (_health.Value <= 0)
        {
            _health.Value = 0;
            pRagdoll.ActivateRagdoll();
        }
    }
    private void OnHealthChange(float prev, float next, bool asServer)
    {
        if (IsOwner && !asServer && pUI != null)
        {
            pUI.UpdateHealth(prev, next);
        }
    }
}