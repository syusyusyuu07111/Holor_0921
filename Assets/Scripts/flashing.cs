using UnityEngine;

public class flashing : MonoBehaviour
{
    [Header("二値フラッシュ（通常ON→一瞬OFF）")]
    [Tooltip("一瞬だけ落とす明るさ（ほぼ真っ暗にしたいなら0）")]
    public float offIntensity = 0f;

    [Header("基本テンポ")]
    public float baseInterval = 5.0f;   // 何秒ごとに一回ビクッと落ちるか（平均）
    [Range(0f, 0.9f)]
    public float intervalJitter = 0.2f; // 間隔のブレ率（±20%とか）

    [Header("消灯してる時間（ブツッ…）")]
    public Vector2 offHold = new Vector2(0.06f, 0.12f); // 暗いままにする時間のランダム幅

    [Header("時間の種類")]
    public bool useUnscaledTime = false; // ポーズ中も動かしたいなら true

    // ==== 内部 ====
    private Light _light;
    private bool _isOffNow = false;   // 今が消灯中か
    private float _tNext;             // 次に状態を切り替える時刻

    // ★「通常時の最新の明るさ」を記録しておく
    //   ・ONの間は毎フレームこれを更新する
    //   ・OFFに落とす瞬間、その時点の値を覚えておく
    //   ・ONに戻すときはこの値に戻す
    private float _lastOnIntensity = 1f;

    void Start()
    {
        _light = GetComponent<Light>();
        if (!_light)
        {
            enabled = false;
            return;
        }

        // ★初期の明るさを拾っておく
        _lastOnIntensity = Mathf.Max(0f, _light.intensity);

        // 最初はON状態からスタート
        _isOffNow = false;
        Apply(_lastOnIntensity);

        // 最初の「一瞬OFFにするタイミング」を仕込む
        _tNext = Now() + NextOnDuration();
    }

    void Update()
    {
        if (!_light) return;

        // ★ON中は常に「いまの自然な明るさ」を追跡する
        //   → ほかの演出でライトのintensityが変化しても拾う
        if (!_isOffNow)
        {
            _lastOnIntensity = Mathf.Max(0f, _light.intensity);
        }

        // タイミング来た？
        if (Now() >= _tNext)
        {
            if (_isOffNow)
            {
                // 今OFF中 → ONに戻す
                _isOffNow = false;

                // ★直前に記録しておいた強さに戻す
                Apply(_lastOnIntensity);

                // 次の「一瞬OFFするまでの時間」を決める
                _tNext = Now() + NextOnDuration();
            }
            else
            {
                // 今ON中 → 一瞬OFFに落とす
                _isOffNow = true;

                // ★OFFに落とす前の強さはもう _lastOnIntensity に入ってる
                //   ここではライトを一気に暗くする
                Apply(offIntensity);

                // どれくらい暗いままにするか（短いビクッ）
                float hold = Random.Range(offHold.x, offHold.y);
                _tNext = Now() + Mathf.Max(0.001f, hold);
            }
        }
    }

    // 次に「一瞬OFFするまでどれくらいONを維持するか」
    // baseInterval に ±jitter かけた値
    float NextOnDuration()
    {
        float jitter = 1f + Random.Range(-intervalJitter, intervalJitter); // 0.8〜1.2みたいな感じ
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
        // OFFの明るさがマイナスはおかしいので補正
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
