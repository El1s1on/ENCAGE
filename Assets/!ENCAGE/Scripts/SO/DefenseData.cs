using UnityEngine;

[CreateAssetMenu]
public class DefenseData : ScriptableObject
{
    public float blockCooldown = 0.5f;

    [Header("Block / Parry")]
    [Tooltip("Seconds after block start where parry is active.")]
    public float parryWindow = 0.12f;

    public float parryStaminaReward = 20f;

    [Tooltip("Dot threshold for front checks. Higher = stricter. 0 means front hemisphere.")]
    [Range(-1f, 1f)]
    public float frontDotThreshold = 0.15f;

    [Tooltip("Dot threshold for backstab (attacker behind victim). Lower = easier backstab.")]
    [Range(-1f, 1f)]
    public float backstabDotThreshold = -0.35f;

    public float blockStamina = 5f;
}
