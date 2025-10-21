using UnityEngine;

public class flashing : MonoBehaviour
{
    // ==== 調整用（Inspector） ====
    [Header("明るさレンジ")]
    public Vector2 intensityRange = new Vector2(0f, 10f);

    [Header("ターゲット更新（不規則）")]
    public Vector2 changeInterval = new Vector2(0.05f, 0.7f); // 次の目標強度へ切り替えるまでの時間
    public Vector2 lerpSpeed = new Vector2(3f, 15f);          // 目標に近づく速度のランダム範囲
    [Range(0f, 1f)] public float goDarkChance = 0.3f;         // 暗めを狙う確率

    [Header("微細ゆらぎ（Perlin）")]
    public float noiseAmount = 0.3f;  // 付加ノイズ量
    public float noiseSpeed = 2.0f;   // ノイズ速度
    public float noiseSeed = 0f;      // 0なら自動

    [Header("たまに完全消灯")]
    [Range(0f, 0.2f)] public float blackoutChance = 0.04f;     // 稀に発生
    public Vector2 blackoutDuration = new Vector2(0.4f, 1.2f); // 消灯時間

    // ==== 内部 ====
    private Light _light;
    private float lightStrength;  // 現在強度（元スクリプトの変数名を流用）
    private float _target;        // 次に向かう強度
    private float _timer;         // 次の切り替えまで
    private float _speed;         // この区間の移動速度
    private bool _inBlackout;    // 完全消灯中か

    void Start()
    {
        _light = GetComponent<Light>();
        if (!_light) { enabled = false; return; }

        // 初期化
        if (noiseSeed == 0f) noiseSeed = Random.value * 1000f;

        intensityRange.x = Mathf.Max(0f, intensityRange.x);
        intensityRange.y = Mathf.Max(intensityRange.x + 0.01f, intensityRange.y);

        // 開始値はレンジ内ランダム
        lightStrength = Random.Range(intensityRange.x, intensityRange.y);
        _target = lightStrength;
        ScheduleNext();
        Apply(lightStrength);
    }

    void Update()
    {
        if (!_light) return;

        // 稀に「長い完全暗転」を差し込む
        if (!_inBlackout && Random.value < blackoutChance * Time.deltaTime)
        {
            _inBlackout = true;
            _target = 0f;
            _speed = Random.Range(lerpSpeed.x, lerpSpeed.y);
            _timer = Random.Range(blackoutDuration.x, blackoutDuration.y);
        }

        // 目標更新タイミング
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            if (_inBlackout)
            {
                // 暗転から復帰 → 通常スケジュール
                _inBlackout = false;
                ScheduleNext();
            }
            else
            {
                // 次の目標強度をランダムに決定（暗めを狙う確率も混ぜる）
                bool goDark = Random.value < goDarkChance;
                float min = intensityRange.x;
                float max = intensityRange.y;
                _target = goDark
                    ? Random.Range(min, Mathf.Lerp(min, max, 0.35f))
                    : Random.Range(Mathf.Lerp(min, max, 0.3f), max);

                _speed = Random.Range(lerpSpeed.x, lerpSpeed.y);
                _timer = Random.Range(changeInterval.x, changeInterval.y);
            }
        }

        // Perlinノイズで微細ゆらぎを付与
        float n = Mathf.PerlinNoise(noiseSeed, Time.time * noiseSpeed) * 2f - 1f;

        // 目標へ不等速で寄せる
        lightStrength = Mathf.MoveTowards(lightStrength, _target, _speed * Time.deltaTime);

        // ノイズ加算＆クランプ
        float finalIntensity = Mathf.Clamp(lightStrength + n * noiseAmount, intensityRange.x, intensityRange.y);

        Apply(finalIntensity);
    }

    // 次の区間の速度・時間・目標を組む
    void ScheduleNext()
    {
        _speed = Random.Range(lerpSpeed.x, lerpSpeed.y);
        _timer = Random.Range(changeInterval.x, changeInterval.y);

        float span = intensityRange.y - intensityRange.x;
        _target = Mathf.Clamp(lightStrength + Random.Range(-span, span) * 0.5f, intensityRange.x, intensityRange.y);
    }

    void Apply(float value)
    {
        _light.intensity = value;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (intensityRange.y < intensityRange.x) intensityRange.y = intensityRange.x + 0.01f;
        if (changeInterval.x < 0f) changeInterval.x = 0f;
        if (changeInterval.y < changeInterval.x) changeInterval.y = changeInterval.x + 0.01f;
        if (lerpSpeed.x < 0f) lerpSpeed.x = 0f;
        if (lerpSpeed.y < lerpSpeed.x) lerpSpeed.y = lerpSpeed.x + 0.01f;
        if (blackoutDuration.x < 0f) blackoutDuration.x = 0f;
        if (blackoutDuration.y < blackoutDuration.x) blackoutDuration.y = blackoutDuration.x + 0.01f;
    }
#endif
}
