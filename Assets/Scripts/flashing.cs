using UnityEngine;

public class flashing : MonoBehaviour
{
    // ==== 調整用（Inspector） ====
    [Header("二値フラッシュ（通常ON→一瞬OFF）")]
    public float onIntensity = 100f;    // ふだんの明るさ（ついてる状態）
    public float offIntensity = 0f;     // 一瞬だけ落とす明るさ（ほぼ真っ暗）

    [Header("基本テンポ")]
    public float baseInterval = 5.0f;   // 何秒ごとに一回ビクッと落ちるか（平均）
    [Range(0f, 0.9f)] public float intervalJitter = 0.2f; // 間隔のブレ率（±20%とか）

    [Header("消灯してる時間（ブツッ…）")]
    public Vector2 offHold = new Vector2(0.06f, 0.12f); // 暗いままにする時間のランダム幅

    [Header("時間の種類")]
    public bool useUnscaledTime = false; // ポーズ中も動かすなら true

    // ==== 内部 ====
    private Light _light;
    private bool _isOffNow = false; // 今が消灯中か
    private float _tNext;           // 次に状態を切り替える時刻

    void Start()
    {
        _light = GetComponent<Light>();
        if (!_light)
        {
            enabled = false;
            return;
        }

        // スタート時は「通常点いてる」
        _isOffNow = false;
        Apply(onIntensity);

        // 最初の「一瞬落ちるタイミング」を予約
        _tNext = Now() + NextOnDuration();
    }

    void Update()
    {
        if (!_light) return;

        if (Now() >= _tNext)
        {
            if (_isOffNow)
            {
                // 今OFF中 → ONに戻す
                _isOffNow = false;
                Apply(onIntensity);

                // 次の「一瞬OFF」が来るタイミングを決める（数秒〜）
                _tNext = Now() + NextOnDuration();
            }
            else
            {
                // 今ON中 → 一瞬だけOFFに落とす
                _isOffNow = true;
                Apply(offIntensity);

                // どれくらい落ちたままにするか（短い）
                float hold = Random.Range(offHold.x, offHold.y);
                _tNext = Now() + Mathf.Max(0.001f, hold);
            }
        }
    }

    // 次に「一瞬OFFするまでどれくらいONを維持するか」
    // baseInterval に ±jitter かけた値
    float NextOnDuration()
    {
        float jitter = 1f + Random.Range(-intervalJitter, intervalJitter); // 0.8〜1.2とか
        float dur = Mathf.Max(0.001f, baseInterval * jitter);
        return dur;
    }

    // 現在時刻（scaled / unscaled 切替）
    float Now()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }

    // Lightの明るさだけ切り替える（スナップ、補間なし）
    void Apply(float value)
    {
        _light.intensity = value;
    }

    void OnValidate()
    {
        // 明るさの妥当化
        if (onIntensity < 0f) onIntensity = 0f;
        if (offIntensity < 0f) offIntensity = 0f;

        // OFF時間の妥当化
        if (offHold.x < 0f) offHold.x = 0f;
        if (offHold.y < offHold.x) offHold.y = offHold.x + 0.001f;

        // 間隔の妥当化
        if (baseInterval < 0.01f) baseInterval = 0.01f;
        if (intervalJitter < 0f) intervalJitter = 0f;
        if (intervalJitter > 0.9f) intervalJitter = 0.9f;
    }
}
