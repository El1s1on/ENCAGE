using UnityEngine;
using TMPro;
using UnityEngine.UI;
using FishNet.Object;
using System.Collections;
using PrimeTween;

public class PlayerUI : NetworkBehaviour
{
    private TMP_Text healthText;

    // Token: 0x04000157 RID: 343
    private TMP_Text quickStateText;

    // Token: 0x04000158 RID: 344
    private Image staminaBar;

    // Token: 0x04000159 RID: 345
    private GameObject staminaPanel;

    // Token: 0x0400015A RID: 346
    private Transform canvasObject;

    // Token: 0x0400015B RID: 347
    private PlayerMovement pMovement;

    // Token: 0x0400015C RID: 348
    private float lastStaminaValue;

    // Token: 0x0400015D RID: 349
    private float timeSinceLastStaminaChange;

    // Token: 0x0400015E RID: 350
    private bool isStaminaPanelVisible = true;

    // Token: 0x0400015F RID: 351
    [SerializeField]
    private float hideDelay = 2f;

    // Token: 0x04000160 RID: 352
    [SerializeField]
    private float fadeSpeed = 5f;

    // Token: 0x04000161 RID: 353
    private Tween positionShakeTween;

    // Token: 0x04000162 RID: 354
    private Sequence mainSequence;

    // Token: 0x04000163 RID: 355
    private Vector3 originalPosition;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
            pMovement = GetComponent<PlayerMovement>();
    }

    public void SettinUpCanvas(Transform camera)
    {
        canvasObject = camera.Find("Canvas");
        healthText = canvasObject.Find("Health").GetComponent<TMP_Text>();

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