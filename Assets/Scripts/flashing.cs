using UnityEngine;

public class flashing : MonoBehaviour
{
    //======================================================================
    // 設定：フラッシュの「落とし先」
    //======================================================================
    [Header("二値フラッシュ（通常ON→一瞬OFF）")]
    [Tooltip("一瞬だけ落とす明るさ（ほぼ真っ暗にしたいなら0）")]
    public float offIntensity = 0f;

    //======================================================================
    // 設定：基本テンポ（平均間隔 + ブレ）
    //======================================================================
    [Header("基本テンポ")]
    public float baseInterval = 5.0f;       // 平均何秒ごとに「一瞬OFF」するか
    [Range(0f, 0.9f)]
    public float intervalJitter = 0.2f;     // 間隔の揺らぎ率（±20%など）

    //======================================================================
    // 設定：OFF中の保持時間（ブツッ…）
    //======================================================================
    [Header("消灯してる時間（ブツッ…）")]
    public Vector2 offHold = new Vector2(0.06f, 0.12f); // 暗い時間のランダム幅（秒）

    //======================================================================
    // 設定：時間の参照（ポーズ中でも動かしたいなら unscaled）
    //======================================================================
    [Header("時間の種類")]
    public bool useUnscaledTime = false;     // true: Time.unscaledTime / false: Time.time

    //======================================================================
    // 内部状態
    //======================================================================
    private Light _light;                    // 対象ライト
    private bool _isOffNow = false;          // 現在「OFF状態」か
    private float _tNext;                    // 次に状態を切り替える時刻

    // 「通常時（ON時）の明るさ」を覚える
    // ・ON中は毎フレーム追従して更新（他演出でintensity変化しても追える）
    // ・OFFに落とした後、ONに戻す時はこの値へ復帰する
    private float _lastOnIntensity = 1f;

    //======================================================================
    // 初期化
    //======================================================================
    void Start()
    {
        // 同じGameObject上のLightを取得（無いならこのスクリプトは無効化）
        _light = GetComponent<Light>();
        if (!_light)
        {
            enabled = false;
            return;
        }

        // 初期時点の明るさを「復帰先」として保存
        _lastOnIntensity = Mathf.Max(0f, _light.intensity);

        // 初期はON状態
        _isOffNow = false;
        Apply(_lastOnIntensity);

        // 次にOFFへ落とすタイミングを予約
        _tNext = Now() + NextOnDuration();
    }

    //======================================================================
    // 更新（点滅のタイミング制御）
    //======================================================================
    void Update()
    {
        if (!_light) return;

        // ON中は「現在の自然なintensity」を常に追跡しておく
        // → 例えば別スクリプトでライトの強さを変えても、復帰先が追従する
        if (!_isOffNow)
        {
            _lastOnIntensity = Mathf.Max(0f, _light.intensity);
        }

        // 次の切り替え時刻に到達したか？
        if (Now() >= _tNext)
        {
            if (_isOffNow)
            {
                //========================
                // OFF → ON に戻す
                //========================
                _isOffNow = false;

                // 直前に記録していた「通常の明るさ」へ戻す
                Apply(_lastOnIntensity);

                // 次の「一瞬OFF」までの時間を予約
                _tNext = Now() + NextOnDuration();
            }
            else
            {
                //========================
                // ON → OFF に落とす
                //========================
                _isOffNow = true;

                // 一瞬で指定値まで落とす（補間なし）
                Apply(offIntensity);

                // 暗いままにする時間をランダムで決めて予約
                float hold = Random.Range(offHold.x, offHold.y);
                _tNext = Now() + Mathf.Max(0.001f, hold);
            }
        }
    }

    //======================================================================
    // 次の「ON維持時間」（baseIntervalにジッタを掛ける）
    //======================================================================
    float NextOnDuration()
    {
        // 例：intervalJitter=0.2 → 0.8〜1.2倍
        float jitter = 1f + Random.Range(-intervalJitter, intervalJitter);
        float dur = Mathf.Max(0.001f, baseInterval * jitter);
        return dur;
    }

    //======================================================================
    // 現在時刻（scaled / unscaled 切り替え）
    //======================================================================
    float Now()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }

    //======================================================================
    // Lightの明るさを即時反映（スナップ）
    //======================================================================
    void Apply(float value)
    {
        _light.intensity = value;
    }

    //======================================================================
    // Inspector上の入力値を安全な範囲に丸める
    //======================================================================
    void OnValidate()
    {
        // OFFの明るさは0以上
        if (offIntensity < 0f) offIntensity = 0f;

        // OFF保持時間：x>=0 / y>=x
        if (offHold.x < 0f) offHold.x = 0f;
        if (offHold.y < offHold.x) offHold.y = offHold.x + 0.001f;

        // 基本間隔：最低値を確保
        if (baseInterval < 0.01f) baseInterval = 0.01f;

        // ジッタ：0〜0.9に制限
        if (intervalJitter < 0f) intervalJitter = 0f;
        if (intervalJitter > 0.9f) intervalJitter = 0.9f;
    }
}