using FishNet.Component.Animating;
using FishNet.Object;
using UnityEngine;

public class WeaponViewModel : NetworkBehaviour
{
    [SerializeField] private float rotationSpeed;
    [SerializeField] private Animator animator;
    private PlayerCamera pCamera;

    private readonly int hashOwnAttackParried = Animator.StringToHash("OwnAttackParried");
    private readonly int hashDirection = Animator.StringToHash("Direction");
    private readonly int hashCharge = Animator.StringToHash("Charge");
    private readonly int hashBlock = Animator.StringToHash("Block");
    public override void OnStartClient()
    {
        base.OnStartClient();

        SkinnedMeshRenderer meshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        meshRenderer.enabled = IsOwner;

        if (!IsOwner) return;

        GameObject player = transform.root.gameObject;
        pCamera = player.GetComponent<PlayerCamera>();
    }

    private void Update()
    {
        if (!IsOwner) return;


        if (pCamera)
        {
            float dt = Time.deltaTime;
            Quaternion targetRotation = Quaternion.Euler(pCamera.Pitch, 0, 0);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, rotationSpeed * dt);
        }
    }

    public void LightAttack(int dir)
    {
        animator.SetInteger(hashDirection, dir);
        animator.SetBool(hashCharge, false);

        string clip = $"Arms_lightattack{dir}";
        animator.Play(clip, 0, 0f);
    }

    public void HeavyAttack(int dir)
    {
        animator.SetInteger(hashDirection, dir);
        animator.SetBool(hashCharge, false);

        string clip = $"Arms_heavyattack{dir}";
        animator.Play(clip, 0, 0f);
    }

    public void Parry()
    {
        string clip = $"Arms_blockparry";
        animator.Play(clip, 0, 0f);
    }

    public void BlockHit()
    {
        string clip = $"Arms_blockhit";
        animator.Play(clip, 0, 0f);
    }

    public void OwnAttackParried()
    {
        animator.SetTrigger(hashOwnAttackParried);
    }    

    public void SetCharge(bool value, int dir)
    {
        this.animator.SetInteger(this.hashDirection, dir);
        this.animator.SetBool(this.hashCharge, value);
    }

    public void SetBlock(bool value)
    {
        this.animator.SetBool(this.hashBlock, value);
    }
}
