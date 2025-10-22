using UnityEngine;

public class flashing : MonoBehaviour
{
    // ==== 調整用（Inspector） ====
    [Header("二値フラッシュ（0/100）")]
    public float offIntensity = 0f;     // 暗転（0）
    public float onIntensity = 100f;   // 点灯（100）

    [Header("基本テンポ")]
    public float baseInterval = 5.0f;   // 1発ごとの基準間隔（秒）…「5秒に一回」
    [Range(0f, 0.9f)] public float intervalJitter = 0.2f; // 間隔のブレ率（±20%）

    [Header("点灯時間（チカッと）")]
    public Vector2 onHold = new Vector2(0.06f, 0.12f); // 明るい状態の保持時間（短め）

    [Header("時間の種類")]
    public bool useUnscaledTime = false; // ポーズ中も動かすなら true

    // ==== 内部 ====
    private Light _light;
    private float lightStrength;   // 現在強度（0 or 100）
    private bool _isOn;           // 現在が点灯か
    private float _tNext;          // 次に切り替える時刻

    void Start()
    {
        _light = GetComponent<Light>();
        if (!_light) { enabled = false; return; }

        // 初期化（開始は暗転）
        _isOn = false;
        lightStrength = offIntensity;
        Apply(lightStrength);

        // 最初の「次の点灯」を予約（= baseInterval 付近で1発目）
        _tNext = Now() + NextOffDuration();
    }

    void Update()
    {
        if (!_light) return;

        if (Now() >= _tNext)
        {
            if (_isOn)
            {
                // 点灯終了 → 消灯に戻す → 次の点灯までの“長い間”を予約
                _isOn = false;
                lightStrength = offIntensity;
                Apply(lightStrength);
                _tNext = Now() + NextOffDuration();
            }
            else
            {
                // 消灯 → 一瞬だけ点灯 → 点灯終わり時刻を予約
                _isOn = true;
                lightStrength = onIntensity;
                Apply(lightStrength);
                _tNext = Now() + Random.Range(onHold.x, onHold.y);
            }
        }
    }

    // 次の「消灯中の長い間隔」を決める（5秒±ジッター）
    float NextOffDuration()
    {
        float jitter = 1f + Random.Range(-intervalJitter, intervalJitter);
        float dur = Mathf.Max(0.001f, baseInterval * jitter);
        // onHold 分は“点灯側”で消費するので、そのまま dur を使う（= チカッが5秒ごとに来る体感）
        return dur;
    }

    // 現在時刻（scaled / unscaled 切替）
    float Now() => useUnscaledTime ? Time.unscaledTime : Time.time;

    // 強度を反映（完全二値：補間なし）
    void Apply(float value)
    {
        _light.intensity = value;
    }

    void OnValidate()
    {
        // 強度の妥当化
        if (offIntensity < 0f) offIntensity = 0f;
        if (onIntensity < 0f) onIntensity = 0f;

        // 点灯時間の妥当化
        if (onHold.x < 0f) onHold.x = 0f;
        if (onHold.y < onHold.x) onHold.y = onHold.x + 0.001f;

        // 間隔の妥当化
        if (baseInterval < 0.01f) baseInterval = 0.01f;
        if (intervalJitter < 0f) intervalJitter = 0f;
        if (intervalJitter > 0.9f) intervalJitter = 0.9f;
    }
}
