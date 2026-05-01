using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour
{
    private Inputs actions;
    private PlayerCamera playerCamera;

    public Vector2 Move { get; private set; }
    public Vector2 Look { get; private set; }

    private bool jumpPressed;
    private bool dashPressed;
    private bool debugPressed;

    public bool SprintHeld { get; private set; }
    public bool CrouchHeld { get; private set; }

    public Action onStartM1;
    public Action onReleaseM1;

    public Action onStartM2;
    public Action onReleaseM2;

    private void Awake()
    {
        actions = new Inputs();
        playerCamera = GetComponent<PlayerCamera>();
    }

    private void OnEnable()
    {
        actions.Enable();
        actions.Player.Move.performed += ctx => Move = ctx.ReadValue<Vector2>();
        actions.Player.Move.canceled += ctx => Move = Vector2.zero;

        actions.Player.Look.performed += ctx => Look = ctx.ReadValue<Vector2>();
        actions.Player.Look.canceled += ctx => Look = Vector2.zero;

        actions.Player.Jump.performed += _ => jumpPressed = true;
        actions.Player.Dash.performed += _ => dashPressed = true;
        actions.Player.DEBUG.performed += _ => debugPressed = true;

        actions.Player.Sprint.performed += _ => SprintHeld = true;
        actions.Player.Sprint.canceled += _ => SprintHeld = false;

        actions.Player.Crouch.performed += _ => CrouchHeld = true;
        actions.Player.Crouch.canceled += _ => CrouchHeld = false;

        actions.Player.M1.performed += _ => onStartM1?.Invoke();
        actions.Player.M1.performed -= _ => onReleaseM1?.Invoke();

        actions.Player.M2.performed += _ => onStartM2?.Invoke();
        actions.Player.M2.performed -= _ => onReleaseM2?.Invoke();
    }

    private void OnDisable()
    {
        actions.Disable();
    }

    public PlayerInputData ConsumeInput()
    {
        PlayerInputData data = new PlayerInputData
        {
            Move = Move,
            Yaw = playerCamera != null ? playerCamera.Yaw : 0f,
            Jump = jumpPressed,
            Dash = dashPressed,
            Debug = debugPressed,
            Sprint = SprintHeld,
            Crouch = CrouchHeld
        };

        jumpPressed = false;
        dashPressed = false;
        debugPressed = false;

        return data;
    }
}