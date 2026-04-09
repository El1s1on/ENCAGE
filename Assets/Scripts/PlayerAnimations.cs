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
    private readonly int hashJump = Animator.StringToHash("Jump");
    private readonly int hashLand = Animator.StringToHash("Land");
    private readonly int hashLightAttack = Animator.StringToHash("LightAttack");
    private readonly int hashIsGrounded = Animator.StringToHash("Grounded");

    private bool _forceImmediateIdleNextFrame = false;
    public void SetMovementState(float dirX, float dirZ, bool isGrounded, bool isSprinting, float dt)
    {
        if (!IsServerStarted) return;

        animator.SetFloat(hashDirX, dirX, blendDampTime, dt);
        animator.SetFloat(hashDirZ, dirZ, blendDampTime, dt);
        animator.SetBool(hashIsGrounded, isGrounded);
        //animator.SetBool(hashIsSprinting, isSprinting);
    }

    public void ForceGroundedIdle()
    {
        if (animator == null) return;

        animator.SetFloat(hashDirX, 0f, 0f, 0f);
        animator.SetFloat(hashDirZ, 0f, 0f, 0f);
        animator.SetBool(hashIsGrounded, true);

        animator.ResetTrigger(hashJump);
        animator.ResetTrigger(hashLand);

        animator.Play("Land", 0, 0f);
        animator.Update(0f);

        if (IsServerStarted && networkAnimator != null)
        {
            networkAnimator.SendAll();
            networkAnimator.Play("Land", 0, 0f);
        }
    }

    private void LateUpdate()
    {
        if (_forceImmediateIdleNextFrame)
        {
            animator.SetFloat(hashDirX, 0f, 0f, 0f);
            animator.SetFloat(hashDirZ, 0f, 0f, 0f);
            _forceImmediateIdleNextFrame = false;
        }
    }

    public void TriggerJump()
    {
        if (IsServerStarted) networkAnimator.SetTrigger(hashJump);
    }

    public void TriggerLand()
    {
        if (IsServerStarted) networkAnimator.SetTrigger(hashLand);
    }

    public void TriggerDash()
    {
        //if (IsServerStarted) networkAnimator.SetTrigger(hashDash);
    }

    public void TriggerLightAttack()
    {
        if (IsServerStarted) networkAnimator.SetTrigger(hashLightAttack);
    }
}
