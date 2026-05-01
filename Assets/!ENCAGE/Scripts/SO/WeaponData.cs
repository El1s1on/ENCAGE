using UnityEngine;

[CreateAssetMenu]
public class WeaponData : ScriptableObject
{
    public string weaponName;
    public AttackData lightAttack;
    public AttackData heavyAttack;
    public DefenseData defense;
}
