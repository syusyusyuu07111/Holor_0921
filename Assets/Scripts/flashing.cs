using UnityEngine;

public class flashing : MonoBehaviour
{
    [Header("Intensity Settings")]
    public float offIntensity = 0f;
    public float onIntensity = 100f;

    [Header("Toggle Interval (Seconds)")]
    public Vector2 changeInterval = new Vector2(0.05f, 0.7f);

    private Light _light;
    private float _timer;
    private bool _isOn;

    void Start()
    {
        _light = GetComponent<Light>();
        if (!_light)
        {
            enabled = false;
            return;
        }

        offIntensity = Mathf.Max(0f, offIntensity);
        onIntensity = Mathf.Max(offIntensity, onIntensity);

        _isOn = Random.value > 0.5f;
        ScheduleNext();
        Apply(_isOn ? onIntensity : offIntensity);
    }

    void Update()
    {
        if (!_light) return;

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            Toggle();
            ScheduleNext();
        }
    }

    void ScheduleNext()
    {
        _timer = Random.Range(changeInterval.x, changeInterval.y);
    }

    void Toggle()
    {
        _isOn = !_isOn;
        Apply(_isOn ? onIntensity : offIntensity);
    }

    void Apply(float value)
    {
        _light.intensity = value;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (changeInterval.x < 0f) changeInterval.x = 0f;
        if (changeInterval.y < changeInterval.x) changeInterval.y = changeInterval.x + 0.01f;
        offIntensity = Mathf.Max(0f, offIntensity);
        onIntensity = Mathf.Max(offIntensity, onIntensity);
    }
#endif
}
