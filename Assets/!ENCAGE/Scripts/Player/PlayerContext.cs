using FishNet.Object;
using UnityEngine;
using System.Runtime.CompilerServices;

public class PlayerContext : NetworkBehaviour
{
    public CharacterController CharacterController { get; private set; }
    public HitboxGroup HitboxGroup { get; private set; }
    public PlayerAnimations Animations { get; private set; }
    public PlayerFootsteps Footsteps { get; private set; }
    public PlayerMovement Movement { get; private set; }
    public PlayerRagdoll Ragdoll { get; private set; }
    public PlayerCamera Camera { get; private set; }
    public PlayerCombat Combat { get; private set; }
    public PlayerHealth Health { get; private set; }
    public PlayerInput Input { get; private set; }
    public PlayerUI UI { get; private set; }

    private static readonly ConditionalWeakTable<NetworkObject, PlayerContext> _map = new();
    public bool IsLocalPlayer => Owner != null && Owner.IsLocalClient;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _map.Remove(NetworkObject);
        _map.Add(NetworkObject, this);

        CacheComponents();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();

        _map.Remove(NetworkObject);

        if (Input)
            Input.Deinitialize();
    }

    public static PlayerContext Get(NetworkObject networkObject)
    {
        if (networkObject == null)
            return null;

        _map.TryGetValue(networkObject, out var ctx);
        return ctx;
    }

    private void CacheComponents()
    {
        CharacterController = GetComponent<CharacterController>();
        HitboxGroup = GetComponent<HitboxGroup>();
        Animations = GetComponent<PlayerAnimations>();
        Footsteps = GetComponent<PlayerFootsteps>();
        Movement = GetComponent<PlayerMovement>();
        Ragdoll = GetComponent<PlayerRagdoll>();
        Combat = GetComponent<PlayerCombat>();
        Camera = GetComponent<PlayerCamera>();
        Health = GetComponent<PlayerHealth>();
        Input = GetComponent<PlayerInput>();
        UI = GetComponent<PlayerUI>();

        Input.Initialize(this);
    }
}