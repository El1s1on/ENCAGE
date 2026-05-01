using FishNet.Object;
using UnityEngine;

public class WeaponViewModel : NetworkBehaviour
{
    [SerializeField] private float rotationSpeed;
    [SerializeField] private Animator animator;
    private PlayerCamera pCamera;

    private readonly int hashLightAttack = Animator.StringToHash("LightAttack");
    private readonly int hashDirection = Animator.StringToHash("Direction");
    private readonly int hashCharge = Animator.StringToHash("Charge");
    private readonly int hashHeavyAttack = Animator.StringToHash("HeavyAttack");
    private readonly int hashBlockParry = Animator.StringToHash("BlockParry");
    private readonly int hashBlock = Animator.StringToHash("Block");
    private readonly int hashBlockHit = Animator.StringToHash("BlockHit");
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
        this.animator.SetInteger(this.hashDirection, dir);
        this.animator.SetBool(this.hashCharge, false);
        this.animator.SetTrigger(this.hashLightAttack);
    }

    public void HeavyAttack(int dir)
    {
        this.animator.SetInteger(this.hashDirection, dir);
        this.animator.SetBool(this.hashCharge, false);
        this.animator.SetTrigger(this.hashHeavyAttack);
    }
    
    public void Parry()
    {
        this.animator.SetTrigger(this.hashBlockParry);
    }

    public void BlockHit()
    {
        this.animator.SetTrigger(this.hashBlockHit);
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
