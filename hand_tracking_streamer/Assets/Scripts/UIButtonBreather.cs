using UnityEngine;
using UnityEngine.UI;

public class UIButtonBreather : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Color _baseColor = Color.white;
    [SerializeField] private Color _glowColor = new Color(0xDF / 255f, 0xFF / 255f, 0xFB / 255f); // Cyan-ish
    [SerializeField] private float _speed = 2.0f;

    private Image _buttonImage;

    void Awake()
    {
        _buttonImage = GetComponent<Image>();
    }

    void Update()
    {
        // Calculate a value between 0 and 1 over time using a Cosine wave
        // We use (cos + 1) / 2 to map the -1 to 1 range of Cos to 0 to 1
        float t = (Mathf.Cos(Time.time * _speed) + 1.0f) / 2.0f;

        // Interpolate between the two colors
        if (_buttonImage != null)
        {
            _buttonImage.color = Color.Lerp(_baseColor, _glowColor, t);
        }
    }
}