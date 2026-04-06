using UnityEngine;
using TMPro; // Required for TextMeshPro

public class FadeText : MonoBehaviour
{
    public TextMeshProUGUI textToFade;
    public float fadeSpeed = 1.5f;

    void Update()
    {
        // PingPong returns a value that moves back and forth between 0 and 1 over time
        float alpha = Mathf.PingPong(Time.unscaledTime * fadeSpeed, 1f);

        // Update the alpha channel of the text color
        Color newColor = textToFade.color;
        newColor.a = alpha;
        textToFade.color = newColor;
    }
}