using FishNet.Object;
using UnityEngine;

public class PlayerContext : NetworkBehaviour
{
    public PlayerAnimations Animations { get; private set; }
    public PlayerFootsteps Footsteps { get; private set; }
    public PlayerMovement Movement { get; private set; }
    public PlayerRagdoll Ragdoll { get; private set; }
    public PlayerCamera Camera { get; private set; }
    public PlayerCombat Combat { get; private set; }
    public PlayerHealth Health { get; private set; }
    public PlayerInput Input { get; private set; }
    public PlayerUI UI { get; private set; }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        CacheComponents();
    }

    private void CacheComponents()
    {
        Animations = GetComponent<PlayerAnimations>();
        Footsteps = GetComponent<PlayerFootsteps>();
        Movement = GetComponent<PlayerMovement>();
        Ragdoll = GetComponent<PlayerRagdoll>();
        Combat = GetComponent<PlayerCombat>();
        Camera = GetComponent<PlayerCamera>();
        Health = GetComponent<PlayerHealth>();
        Input = GetComponent<PlayerInput>();
        UI = GetComponent<PlayerUI>();
    }
}
