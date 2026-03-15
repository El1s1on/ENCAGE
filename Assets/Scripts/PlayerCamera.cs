using FishNet.Object;
using UnityEngine;

public class PlayerCamera : NetworkBehaviour
{
    [Header("Main")]
    [SerializeField] private Camera cameraPrefab;
    [Tooltip("Пустышка внутри Graphical_Object (например, на уровне глаз)")]
    [SerializeField] private Transform visualCameraTarget;

    [Header("Sensitivity")]
    [SerializeField] private float sensitivity = 0.1f; // Настройте под новую инпут систему
    [SerializeField] private float pitchLimit = 85f;

    private PlayerInput pInput;
    private PlayerUI pUI;
    private Transform mainCamera;

    // Храним абсолютные углы
    public float Yaw { get; private set; }
    private float pitch;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner) return;

        pInput = GetComponent<PlayerInput>();
        pUI = GetComponent<PlayerUI>();
        mainCamera = Instantiate(cameraPrefab).transform;
        pUI.SettinUpCanvas(mainCamera);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Инициализируем стартовый угол
        Yaw = transform.eulerAngles.y;
    }

    private void Update()
    {
        if (!IsOwner || !mainCamera) return;

        // В новой инпут системе pInput.Look уже содержит дельту за кадр
        Yaw += pInput.Look.x * sensitivity;
        pitch -= pInput.Look.y * sensitivity;
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        // Вращаем саму камеру (для обзора вверх/вниз и влево/вправо)
        mainCamera.rotation = Quaternion.Euler(pitch, Yaw, 0f);
    }

    private void LateUpdate()
    {
        if (!IsOwner || !mainCamera) return;

        // HARD LOCK (согласно Engineering Brief):
        // Никакого SmoothDamp. VisualCameraTarget УЖЕ сглажен NetworkTickSmoother-ом.
        mainCamera.position = visualCameraTarget.position;
    }
}
