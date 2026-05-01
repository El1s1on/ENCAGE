# FishNet Multiplayer Project — Agent Instructions

## 🚨 CRITICAL: This project uses Fish-Networking, NOT Mirror

FishNet and Mirror are **NOT compatible**. Code written for Mirror WILL NOT WORK.

## RPC Types — Correct Syntax

| Action | Wrong (Mirror) | Correct (FishNet) |
|--------|----------------|-------------------|
| Client → Server | `[Command]` | `[ServerRpc]` |
| Server → All Observers | `[ClientRpc]` | `[ObserversRpc]` |
| Server → Specific Client | `[TargetRpc]` | `[TargetRpc]` |

## ServerRpc — Client to Server

```csharp
[ServerRpc]
private void RpcSendChat(string msg)
{
    Debug.Log($"Received {msg} on the server.");
}

// Get caller info
[ServerRpc]
private void RpcSendChat(string msg, NetworkConnection conn = null)
{
    Debug.Log($"Received {msg} from client {conn.ClientId}");
}

// Allow ANY client to call (not just owner)
[ServerRpc(RequireOwnership = false)]
private void RpcAttack(int targetId) { }

// Run locally on client too
[ServerRpc(RunLocally = true)]
private void RpcDoSomething()
{
    // Runs on both client and server
}
```
ObserversRpc — Server to Observing Clients
```csharp
[ObserversRpc]
private void RpcUpdateHealth(int health) { }

// Don't send to object owner
[ObserversRpc(ExcludeOwner = true)]
private void RpcPlayEffect(Vector3 pos) { }

// Buffer for late-joiners
[ObserversRpc(BufferLast = true)]
private void RpcSetAmmo(int ammo) { }
```
TargetRpc — Server to Specific Client
```csharp
[TargetRpc]
private void TargetRPC_OnOwnBlockHit(NetworkConnection conn, int otherPlayerObjectId, Vector3 hitPoint, bool wasHeavy);
{

}

[Server]
public void ServerNotifyOwnBlockHit(int otherPlayerObjectId, Vector3 hitPoint, bool wasHeavy)
{
        TargetRPC_OnOwnBlockHit(base.Owner, otherPlayerObjectId, hitPoint, wasHeavy);
}

```
SyncVar — Synchronized Variables
FishNet uses SyncVar<T> generic type, NOT attributes!

```csharp
using FishNet.Object;
using FishNet.Object.Synchronizing;

// Basic SyncVar
private readonly SyncVar<int> _health = new SyncVar<int>();

// With settings
private readonly SyncVar<float> _mana = new SyncVar<float>(
    new SyncTypeSettings(1f)  // Send once per second
);

// With change callback
private readonly SyncVar<string> _name = new SyncVar<string>();

private void Awake()
{
    _name.OnChange += (prev, next, asServer) => {
        Debug.Log($"Name: {prev} -> {next}");
    };
}

// Server-side change
[Server]
private void SetHealth(int value)
{
    _health.Value = value;  // Note: .Value
}

// Client request
[ServerRpc]
private void RequestHeal()
{
    _health.Value += 10;
}
```
SyncTypeSettings options:
Parameter   Values
SendRate    float (0 = every tick)
Channel Channel.Reliable / Channel.Unreliable
WritePermission ServerOnly / ClientUnsynchronized
ReadPermission  OwnerOnly / Observers / ExcludeOwner
Client-authoritative SyncVar:

```csharp
private readonly SyncVar<string> _nickname = new SyncVar<string>(
    new SyncTypeSettings
    {
        WritePermission = WritePermission.ClientUnsynchronized,
        ReadPermission = ReadPermission.ExcludeOwner
    }
);

[ServerRpc(RunLocally = true)]
private void SetNickname(string value)
{
    _nickname.Value = value;
}
```
For reference types (classes):
```csharp
private readonly SyncVar<MyClass> _data = new SyncVar<MyClass>();

[Server]
private void UpdateData()
{
    _data.Value.SomeField = 10;
    _data.Dirty();  // Mark for sync after modifying fields
}
```
❌ Don't use (Mirror style):
```csharp
[SyncVar]  // ❌ Attribute doesn't exist in FishNet
private int health;

_health = 100;  // ❌ Missing .Value
```
NetworkBehaviour Base Class
```csharp
using FishNet.Object;

public class PlayerController : NetworkBehaviour
{
    private void Update()
    {
        // Owner check (for client input)
        if (!IsOwner) return;
        
        // Basic initialization properties
        if (IsServerInitialized) 
        { 
            // Server logic (validation, damage calculation)
        }
        
        if (IsClientInitialized) 
        { 
            // Client logic (UI, visual effects)
        }
        
        // Pure server/client (excluding host)
        if (IsServerOnlyInitialized)
        {
            // Pure server only (not host)
        }
        
        if (IsClientOnlyInitialized)
        {
            // Pure client only (not host)
        }
        
        // Started status properties
        if (IsServerStarted)
        {
            // Server is started (including host)
        }
        
        if (IsClientStarted)
        {
            // Client is started (including host)
        }
        
        if (IsServerOnlyStarted)
        {
            // Only pure server is started
        }
        
        if (IsClientOnlyStarted)
        {
            // Only pure client is started
        }
        
        // Host mode (server + client in same process)
        if (IsHostStarted)
        {
            // Logic that only works in host mode
        }
    }
}
```
Golden rule: Never trust the client. All critical logic must run on server.

```csharp
// Client calls this
private void Update()
{
    if (Input.GetKeyDown(KeyCode.Space) && IsOwner)
        RpcRequestAttack();
}

// Server executes and validates
[ServerRpc]
private void RpcRequestAttack()
{
    if (!CanAttack()) return;  // Validate!
    ApplyDamage();              // Critical logic
    RpcOnAttackPerformed();    // Notify all
}

[ObserversRpc]
private void RpcOnAttackPerformed()
{
    // Visual effects on all clients
}
```

DOCUMENTATION LINK FOR MORE INFO:
https://github.com/FirstGearGames/FishNet-Documentation