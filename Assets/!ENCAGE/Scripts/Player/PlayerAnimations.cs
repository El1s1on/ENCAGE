using FishNet.Component.Animating;
using FishNet.Object;
using RootMotion.FinalIK;
using UnityEngine;

public class PlayerAnimations : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private NetworkAnimator networkAnimator;
    [SerializeField] private Transform aimIKHolder;

    [Header("Animation Smoothing")]
    [SerializeField] private float blendDampTime = 0.1f;

    private readonly int hashDirX = Animator.StringToHash("DirX");
    private readonly int hashDirZ = Animator.StringToHash("DirZ");
    private readonly int hashJump = Animator.StringToHash("Jump");
    private readonly int hashLand = Animator.StringToHash("Land");
    private readonly int hashIsGrounded = Animator.StringToHash("Grounded");
    private readonly int hashIsRunning = Animator.StringToHash("Running");

    private readonly int hashLightAttack = Animator.StringToHash("LightAttack");
    private readonly int hashHeavyAttack = Animator.StringToHash("HeavyAttack");
    private readonly int hashDirection = Animator.StringToHash("Direction");
    private readonly int hashBlockHit = Animator.StringToHash("BlockHit");
    private readonly int hashBlockParry = Animator.StringToHash("BlockParry");
    private readonly int hashCharge = Animator.StringToHash("Charge");
    private readonly int hashBlock = Animator.StringToHash("Block");

    public float clampedPitch { get; private set; }

    public void SetMovementState(float dirX, float dirZ, bool isGrounded, bool isSprinting, float dt)
    {
        if (!IsServerStarted) return;

        animator.SetFloat(hashDirX, dirX, blendDampTime, dt);
        animator.SetFloat(hashDirZ, dirZ, blendDampTime, dt);
        animator.SetBool(hashIsGrounded, isGrounded);
        animator.SetBool(hashIsRunning, isSprinting);
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

    public void SetPitch(float pitch)
    {
        if (aimIKHolder == null) return;
        clampedPitch = Mathf.Clamp(pitch, -60f, 90f);

        Vector3 localEuler = aimIKHolder.localEulerAngles;
        localEuler.x = clampedPitch;
        aimIKHolder.localEulerAngles = localEuler;
    }

    public void TriggerJump()
    {
        if (IsServerStarted) networkAnimator.SetTrigger(hashJump);
    }

    public void TriggerLand()
    {
        if (IsServerStarted) networkAnimator.SetTrigger(hashLand);
    }

    [Server]
    public void LightAttack(int dir)
    {
        animator.SetInteger(hashDirection, dir);
        animator.SetBool(hashCharge, false);

        string clip = $"Arms_lightattack{dir}";
        networkAnimator.Play(clip, 1, 0f);
    }

    [Server]
    public void HeavyAttack(int dir)
    {
        animator.SetInteger(hashDirection, dir);
        animator.SetBool(hashCharge, false);

        string clip = $"Arms_heavyattack{dir}";
        networkAnimator.Play(clip, 1, 0f);
    }

    [Server]
    public void Parry()
    {
        string clip = $"Arms_blockparry";
        networkAnimator.Play(clip, 1, 0f);
    }

    [Server]
    public void BlockHit()
    {
        string clip = $"Arms_blockhit";
        networkAnimator.Play(clip, 1, 0f);
    }

    [Server]
    public void SetCharge(bool value, int dir)
    {
        animator.SetInteger(hashDirection, dir);
        animator.SetBool(hashCharge, value);
    }

    [Server]
    public void SetBlock(bool value)
    {
        animator.SetBool(hashBlock, value);
    }
}