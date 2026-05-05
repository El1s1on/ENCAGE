using UnityEngine;
using TMPro;
using UnityEngine.UI;
using FishNet.Object;
using System.Collections;
using PrimeTween;
using FishNet.Connection;

public class PlayerUI : NetworkBehaviour
{
    private TMP_Text healthText;
    private TMP_Text quickStateText;
    private Image staminaBar;
    private GameObject staminaPanel;
    private Transform canvasObject;
    private PlayerMovement pMovement;

    private float lastStaminaValue;
    private float timeSinceLastStaminaChange;
    private bool isStaminaPanelVisible = true;

    [SerializeField] private float hideDelay = 2f;
    [SerializeField] private float fadeSpeed = 5f;

    private Tween positionShakeTween;
    private Sequence mainSequence;
    private Vector3 originalPosition;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
            pMovement = PlayerContext.Get(NetworkObject).Movement;
    }

    public void SettinUpCanvas(Transform camera)
    {
        canvasObject = camera.Find("Canvas");
        healthText = canvasObject.Find("Health").GetComponent<TMP_Text>();
        quickStateText = canvasObject.Find("QuickState").GetComponent<TMP_Text>();
        Transform staminaTransform = canvasObject.Find("Stamina");
        staminaPanel = staminaTransform.gameObject;
        staminaBar = staminaTransform.Find("Fill").GetComponent<Image>();

        if (pMovement)
        {
            lastStaminaValue = pMovement.CurrentStamina;
        }

        ShowStaminaPanel(true);
    }

    public void UpdateHealth(float oldHealth, float newHealth)
    {
        if (healthText == null) return;
        healthText.text = $"{Mathf.RoundToInt(newHealth)}HP";
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (pMovement)
        {
            float currentStamina = pMovement.CurrentStamina;
            float maxStamina = pMovement.MaxStamina;
            float fillAmount = currentStamina / maxStamina;

            if (Mathf.Abs(currentStamina - lastStaminaValue) > 0.01f)
            {
                ShowStaminaPanel(true);
                timeSinceLastStaminaChange = 0f;
                lastStaminaValue = currentStamina;
            }
            else
            {
                timeSinceLastStaminaChange += Time.deltaTime;

                if (timeSinceLastStaminaChange >= hideDelay && isStaminaPanelVisible && currentStamina == maxStamina)
                {
                    ShowStaminaPanel(false);
                }
            }

            staminaBar.fillAmount = Mathf.Lerp(staminaBar.fillAmount, fillAmount, Time.deltaTime * 12f);
        }
    }

    private void ShowStaminaPanel(bool show)
    {
        if (staminaPanel == null) return;

        isStaminaPanelVisible = show;
        StopAllCoroutines();
        StartCoroutine(FadeStaminaPanel(show));
    }

    private IEnumerator FadeStaminaPanel(bool show)
    {
        CanvasGroup canvasGroup = staminaPanel.GetComponent<CanvasGroup>();
        float targetAlpha = show ? 1f : 0f;
        float startAlpha = canvasGroup.alpha;
        float elapsedTime = 0f;

        while (elapsedTime < fadeSpeed)
        {
            elapsedTime += Time.deltaTime;
            canvasGroup.alpha = Mathf.MoveTowards(startAlpha, targetAlpha, elapsedTime / fadeSpeed);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }

    [TargetRpc]
    public void TargetRPC_QuickState(NetworkConnection conn, string text, Color color, float? duration = null)
    {
        QuickState(text, color, duration);
    }

    public void QuickState(string text, Color color, float? duration = null)
    {
        quickStateText.transform.localPosition = originalPosition;

        if (positionShakeTween.isAlive)
            positionShakeTween.Stop();

        if (mainSequence.isAlive)
            mainSequence.Stop();

        quickStateText.text = text;

        color.a = 0f;
        quickStateText.color = color;

        mainSequence = Sequence.Create();

        mainSequence.Chain(Tween.Alpha(quickStateText, 1f, 0.06f, Ease.Linear));

        if (duration.HasValue && duration.Value > 0f)
        {
            positionShakeTween = Tween.ShakeLocalPosition(quickStateText.transform,
                new Vector3(1f, 1f, 0f),
                duration.Value,
                20f);

            mainSequence.Chain(Tween.Delay(duration.Value));

            mainSequence.ChainCallback(() =>
            {
                if (positionShakeTween.isAlive)
                    positionShakeTween.Stop();
                quickStateText.transform.localPosition = originalPosition;
            });

            mainSequence.Chain(Tween.Alpha(quickStateText, 0f, 0.5f, Ease.InOutSine));
        }
        else
        {
            mainSequence.Chain(Tween.Delay(0.3f));
            mainSequence.Chain(Tween.Alpha(quickStateText, 0f, 0.5f, Ease.InOutSine));
        }

        mainSequence.OnComplete(() =>
        {
            quickStateText.transform.localPosition = originalPosition;
        });
    }
}