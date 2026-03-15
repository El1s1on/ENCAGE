using FishNet.Component.Animating;
using FishNet.Object;
using UnityEngine;

public class PlayerAnimations : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private NetworkAnimator networkAnimator;

    [Header("Animation Smoothing")]
    [SerializeField] private float blendDampTime = 0.1f;

    private readonly int hashDirX = Animator.StringToHash("DirX");
    private readonly int hashDirZ = Animator.StringToHash("DirZ");

    public void SetMovementState(float dirX, float dirZ, bool isGrounded, bool isSprinting, float dt)
    {
        if (!IsServerStarted) return;

        animator.SetFloat(hashDirX, dirX, blendDampTime, dt);
        animator.SetFloat(hashDirZ, dirZ, blendDampTime, dt);
        //animator.SetBool(hashIsGrounded, isGrounded);
        //animator.SetBool(hashIsSprinting, isSprinting);
    }

    // --- “–»√√≈–€ ---

    public void TriggerJump()
    {
        //if (IsServerStarted) networkAnimator.SetTrigger(hashJump);
    }

    public void TriggerDash()
    {
        //if (IsServerStarted) networkAnimator.SetTrigger(hashDash);
    }

    public void TriggerMelee()
    {
        //if (IsServerStarted) networkAnimator.SetTrigger(hashMelee);
    }
}
