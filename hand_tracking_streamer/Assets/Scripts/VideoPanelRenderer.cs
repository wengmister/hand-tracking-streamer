using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VideoPanelRenderer : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private RawImage targetRawImage;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private TextMeshProUGUI placeholderText;

    public void SetTexture(Texture texture)
    {
        if (targetRawImage != null) targetRawImage.texture = texture;
        if (targetRenderer != null) targetRenderer.material.mainTexture = texture;
        if (placeholderText != null) placeholderText.text = texture == null ? "No Video" : string.Empty;
    }

    public void SetStatus(string text)
    {
        if (placeholderText != null) placeholderText.text = text;
    }

    public void Clear()
    {
        SetTexture(null);
    }

    public void SetVisible(bool visible)
    {
        GameObject target = panelRoot != null ? panelRoot : gameObject;
        if (target != null)
        {
            target.SetActive(visible);
        }
    }
}
