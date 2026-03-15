using UnityEngine;
using TMPro;

public class PlayerUI : MonoBehaviour
{
    private TMP_Text healthText;
    private Transform canvasObject;

    public void SettinUpCanvas(Transform camera)
    {
        canvasObject = camera.Find("Canvas");
        healthText = canvasObject.Find("Health").GetComponent<TMP_Text>();
    }

    public void UpdateHealth(float oldHealth, float newHealth)
    {
        if (healthText == null) return;
        healthText.text = $"{Mathf.RoundToInt(newHealth)}HP";
    }
}
