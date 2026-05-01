using UnityEngine;

[CreateAssetMenu]
public class AttackData : ScriptableObject
{
    public enum HitShape
    {
        Sphere,
        Capsule
    }

    public float duration = 0.5f;
    public float staminaCost = 7.5f;
    public float staminaRegenDelay = 0.5f;

    public float baseDamage = 7f;
    public float damageVariance = 0.2f;

    [Header("Hit Detection")]
    public HitShape hitShape = HitShape.Sphere;

    [Min(0.001f)]
    public float hitRadius = 0.5f;

    [Tooltip("Capsule length from base to tip (in meters) if tip transform is not provided.")]
    [Min(0f)]
    public float hitLength = 0.9f;

    [Tooltip("Extra offset forward (in meters). This extends range without inflating hitRadius.")]
    public float extraForwardOffset = 0f;

    [Tooltip("Seconds from attack start when hit detection becomes active.")]
    public float hitWindowStart = 0.15f;

    [Tooltip("Seconds from attack start when hit detection stops.")]
    public float hitWindowEnd = 0.35f;

    public Vector3 viewPunch;
}