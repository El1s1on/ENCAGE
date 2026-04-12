using FishNet.Object;
using UnityEngine;

public class WeaponViewModel : NetworkBehaviour
{
    [SerializeField] private float rotationSpeed;
    [SerializeField] private Animator animator;
    private PlayerCamera pCamera;

    private readonly int hashLightAttack = Animator.StringToHash("LightAttack");

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

    public void LightAttack()
    {
        animator.SetTrigger(hashLightAttack);
    }

    public void CancelLightAttack()
    {
        animator.Play("Idle", 1, 0f);
    }
}
