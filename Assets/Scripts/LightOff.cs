using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using CriWare;  // CRI Atom

/*
     ライト切替ギミック（冷色→暖色＆減光 / 暖色→冷色へ復帰）
     ・近づいて入力で切替（レバー演出・見せカメラ演出あり）
     ・暖色化は徐々に色/明るさを補間（TimeScale=0でも進む）
     ・暖色化後に：ゴースト破棄 / 呪いエフェクト破棄 / ヒント進行 / ドア更新 / SE再生
     ・冷色へ戻したらロックして再操作不可にする（仕様）
*/

public class LightOff : MonoBehaviour
{
    //================
    // Inspector参照
    //================

    public GameObject Player;                                   // プレイヤー
    public GameObject Light;                                    // インタラクト地点（スイッチなど）
    public float PushDistance = 3.0f;                           // インタラクト距離
    public bool OnLight = true;                                 // 「いまは明るい(冷色側)？」UI表示用
    public GameObject Ghost;                                    // 暖色化後に消したいオブジェクト（敵とか）

    [Header("レバーON時に消す呪いエフェクト")]
    public List<GameObject> CurseEffects = new List<GameObject>(); // レバーON時に消したい呪いエフェクト

    public GameObject lever;                                    // 回すレバー
    public float RotateLever = 30f;                             // 回す角度（X度）

    [Header("レバー回転")]
    public float LeverRotateSpeed = 180f;                       // 回転速度[deg/sec]
    private bool _isLeverAnimating = false;                     // レバー演出中フラグ

    [Header("レバー音(CRI AtomSource)")]
    public CriAtomSource LeverAtomSource;                       // レバーをガチャっとした時に鳴らす音

    [Header("鍵が開く音(CRI AtomSource)")]
    public CriAtomSource UnlockAtomSource;                      // 鍵が開いた時のSE

    [Header("操作対象ライト群")]
    [SerializeField] private List<Light> LightLists = new List<Light>();

    //================
    // 暖色の最終設定
    //================

    [Header("暖色（最終の色味）")]
    public Color WarmLightColor = new Color(1.0f, 0.78f, 0.56f, 1f);

    [Header("最終の明るさ（元の明るさに対する％）")]
    [Tooltip("例えば10にすると、最終は“最初の明るさの10%”まで落とす")]
    [Range(0f, 200f)]
    public float FinalIntensityPercent = 10f;

    // ライトごとの基準Intensityを保持（初回の暖色化前にキャプチャする）
    private List<float> _baselineIntensities;
    private bool _baselineCaptured = false;

    //================
    // 暖色フェード演出
    //================

    [Header("暖色フェード(演出中はTimeScale=0でも進む)")]
    public float WarmifyLerpDuration = 0.5f;                    // じわーっと変える秒数（実時間）
    public AnimationCurve WarmifyCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);               // 0→1のイージング

    //================
    // 見せカメラ
    //================

    [Header("見せ用カメラ")]
    public Camera MainCamera;                                   // 普段のカメラ（未指定なら Camera.main）
    public Camera ShowcaseCamera;                               // スイッチ演出を見せるカメラ
    public float ShowcaseHoldSeconds = 0.5f;                    // 色が変わったあと見せ続ける秒数(実時間)

    //================
    // 入力 / UI
    //================

    private InputSystem_Actions input;                          // 新InputSystem参照

    public TextMeshProUGUI PromptText;                          // 近づいたら出す「Eで〜」とか
    public TextMeshProUGUI MsgText;                             // 「〜になった」メッセージ

    [Header("文言")]
    public string PromptOn = "【E】暖色にする";                  // まだ冷色で明るい→「暖色にする」
    public string PromptOff = "【E】ライトを点ける";             // すでに暖色で暗い→「戻す」系
    public string MsgTurnedOff = "ライトが暖かい色になった";
    public string MsgTurnedOn = "ライトが点いたようだ";

    [Header("メッセージの表示時間(ゲーム時間)")]
    public float EventMsgDuration = 5.0f;
    private float _msgTimer = 0f;

    //================
    // 進行度・ドア連動
    //================

    [Header("進行度参照")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;

    [Tooltip("暖色化した時に進める量（>=1 推奨）")]
    public int AdvanceAmountOnOff = 1;

    [Tooltip("冷色に戻した時に下げる量（>=1なら下がる）")]
    public int DecreaseAmountOnOn = 0;

    [Tooltip("このライトは最初の暖色化だけカウントするならtrue")]
    public bool CountOnlyOncePerThisLight = false;
    private bool _alreadyCounted = false;

    [Tooltip("連打で多重カウントさせないためのクールダウン秒")]
    public float ToggleDebounceSeconds = 0.25f;
    private float _lastToggleTime = -999f;

    //================
    // ロック / 時間停止
    //================

    // 冷色へ戻した後に再操作させない用途（仕様ロック）
    private bool _lockedOn = false;
    private bool IsLocked() => _lockedOn;

    // レバー演出中だけTimeScale=0にして見せる
    private bool _pausedForLever = false;
    private float _timeScaleBeforePause = 1f;

    //================
    // Tutorial / HintRef キャッシュ
    //================

    private Tutorial _cachedTutorial = null;

    // Tutorial参照を取得（無ければ探す）
    private Tutorial GetTutorial()
    {
        if (_cachedTutorial) return _cachedTutorial;
#if UNITY_2023_1_OR_NEWER
        _cachedTutorial = UnityEngine.Object.FindAnyObjectByType<Tutorial>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
        _cachedTutorial = UnityEngine.Object.FindFirstObjectByType<Tutorial>(FindObjectsInactive.Include);
#else
        _cachedTutorial = UnityEngine.Object.FindObjectOfType<Tutorial>();
#endif
        return _cachedTutorial;
    }

    /*
         HintText参照の保険
         ・Inspector設定が優先
         ・無ければTutorial経由で拾う
         ・それでも無ければシーン内探索（AutoFindHintRef=true時）
    */
    private HintText GetOrResolveHintRef()
    {
        if (HintRef) return HintRef;

        var tut = GetTutorial();
        if (tut && tut.HintRef)
        {
            HintRef = tut.HintRef;
            Debug.Log($"[LightOff] Adopted HintRef from Tutorial. id={HintRef.GetInstanceID()}");
            return HintRef;
        }

        if (AutoFindHintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = UnityEngine.Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            HintRef = UnityEngine.Object.FindFirstObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = UnityEngine.Object.FindObjectOfType<HintText>();
#endif
            if (HintRef)
                Debug.Log($"[LightOff] AutoFound HintRef. id={HintRef.GetInstanceID()}");
        }
        return HintRef;
    }

    //==================================================
    // 基準Intensityをキャプチャ（初回だけ）
    //==================================================

    /*
         冷色（初期状態）の明るさを記録する
         ・暖色化時の「最終 intensity = baseline × FinalIntensityPercent%」に使う
    */
    private void CaptureBaselinesIfNeeded()
    {
        if (_baselineCaptured) return;

        if (_baselineIntensities == null)
            _baselineIntensities = new List<float>(LightLists.Count);
        _baselineIntensities.Clear();

        for (int i = 0; i < LightLists.Count; i++)
        {
            var l = LightLists[i];
            _baselineIntensities.Add(l ? l.intensity : 0f);
        }

        _baselineCaptured = true;
    }

    //==================================================
    // ゲーム停止/再開（Time.timeScaleを0にする演出）
    //==================================================

    // レバー演出の間だけTimeScaleを止める（演出はunscaledDeltaTimeで進める）
    private void PauseGameForLever()
    {
        if (_pausedForLever) return;
        _timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;
        _pausedForLever = true;
    }

    private void ResumeGameIfPausedForLever()
    {
        if (!_pausedForLever) return;
        Time.timeScale = _timeScaleBeforePause;
        _pausedForLever = false;
    }

    //==================================================
    // Unity lifecycle
    //==================================================

    private void Awake()
    {
        input = new InputSystem_Actions();

        if (!MainCamera && Camera.main) MainCamera = Camera.main;
        if (ShowcaseCamera) ShowcaseCamera.enabled = false;      // 最初は見せカメラOFF
    }

    private void OnEnable()
    {
        input.Player.Enable();

        // UI初期化
        if (PromptText) { PromptText.text = ""; PromptText.gameObject.SetActive(false); }
        if (MsgText) { MsgText.text = ""; MsgText.gameObject.SetActive(false); }
    }

    private void OnDisable()
    {
        input.Player.Disable();
    }

    private void OnDestroy()
    {
        // 破棄時の後始末（例外が出ても落とさない）
        try
        {
            if (input != null)
            {
                input.Player.Disable();
                input.UI.Disable();
                input.Dispose();
                input = null;
            }
        }
        catch { }
    }

    private void Update()
    {
        if (!Player || !Light) return;

        //================
        // 距離判定
        //================

        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        bool inRange = (distance < PushDistance);

        //================
        // 近接案内UI
        //================

        // レバー中やロック中は案内を出さない
        if (PromptText) PromptText.gameObject.SetActive(inRange && !_isLeverAnimating && !IsLocked());
        if (inRange && PromptText && !IsLocked())
        {
            // OnLight == true  → まだ冷色側だから「暖色にする」
            // OnLight == false → もう暖色側だから「ライトを点ける」
            PromptText.text = OnLight ? PromptOn : PromptOff;
        }

        //================
        // 入力
        //================

        // 入力(Jumpでインタラクト想定)
        if (inRange && !_isLeverAnimating && !IsLocked() && input.Player.Jump.triggered)
        {
            if (OnLight) TurnOffToWarm();                        // 冷たい白いライト → 暖色＆減光
            else TurnOnCold();                                   // 暖色で暗い → 明るい側に戻す
        }

        //================
        // メッセージの寿命
        //================

        // メッセージはTime.timeScaleの影響を受ける（仕様）
        if (_msgTimer > 0f)
        {
            _msgTimer -= Time.deltaTime;
            if (_msgTimer <= 0f && MsgText)
            {
                MsgText.text = "";
                MsgText.gameObject.SetActive(false);
            }
        }
    }

    //==================================================
    // レバー音再生
    //==================================================

    /*
         レバーSEを鳴らす
         ・AtomSourceが未設定なら lever から拾う
         ・CueはAtomSource側に設定されている前提
    */
    private void PlayLeverSound()
    {
        var src = LeverAtomSource;

        if (!src && lever)
            src = lever.GetComponent<CriAtomSource>();

        if (!src) return;
        src.Play();
    }

    //==================================================
    // 鍵が開く音再生
    //==================================================

    // 鍵開きSE（CueはAtomSource側に設定されている前提）
    private void PlayUnlockSound()
    {
        var src = UnlockAtomSource;
        if (!src) return;
        src.Play();
    }

    //==================================================
    // 冷色→暖色に切り替える（レバー＆カメラ演出込み）
    //==================================================

    /*
         暖色化の開始トリガ
         ・ロック中は無効
         ・連打防止のデバウンス
         ・レバーがある場合は「回転→見せカメラ→暖色化」の流れ
         ・レバーが無い場合は「見せカメラ→暖色化」の流れ
    */
    private void TurnOffToWarm()
    {
        if (IsLocked()) return;
        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        if (lever && RotateLever != 0f)
        {
            if (!_isLeverAnimating)
            {
                PlayLeverSound();
                StartCoroutine(CoRotateLeverThenShowcaseThenWarmify());
            }
        }
        else
        {
            StartCoroutine(CoOnlyShowcaseThenWarmify());
        }
    }

    /*
         レバーあり版
         1) TimeScale停止
         2) レバー回転（unscaledDeltaTime）
         3) 見せカメラON
         4) 暖色＆減光（unscaledDeltaTime）
         5) 見せ続ける（WaitForSecondsRealtime）
         6) カメラ戻す → 副作用処理 → TimeScale復帰
    */
    private IEnumerator CoRotateLeverThenShowcaseThenWarmify()
    {
        _isLeverAnimating = true;
        PauseGameForLever();

        // レバー回転
        Transform tf = lever.transform;
        Vector3 euler = tf.localEulerAngles;
        float startX = euler.x;
        float endX = startX + RotateLever;
        float rotDur = Mathf.Max(0.01f, Mathf.Abs(RotateLever) / Mathf.Max(1f, LeverRotateSpeed));
        float t = 0f;

        while (t < rotDur)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.LerpAngle(startX, endX, Mathf.Clamp01(t / rotDur));
            euler = tf.localEulerAngles; euler.x = x;
            tf.localEulerAngles = euler;
            yield return null;
        }

        // 最終角を固定
        euler = tf.localEulerAngles; euler.x = endX; tf.localEulerAngles = euler;

        SwitchToShowcaseCamera();

        yield return StartCoroutine(CoWarmifyLightsGradually());

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, ShowcaseHoldSeconds));

        SwitchBackToMainCamera();

        AfterWarmifySideEffects();

        _isLeverAnimating = false;
        ResumeGameIfPausedForLever();
    }

    /*
         レバーなし版
         1) TimeScale停止
         2) 見せカメラON
         3) 暖色＆減光（unscaledDeltaTime）
         4) 見せ続ける（WaitForSecondsRealtime）
         5) カメラ戻す → 副作用処理 → TimeScale復帰
    */
    private IEnumerator CoOnlyShowcaseThenWarmify()
    {
        PauseGameForLever();

        SwitchToShowcaseCamera();

        yield return StartCoroutine(CoWarmifyLightsGradually());

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, ShowcaseHoldSeconds));

        SwitchBackToMainCamera();

        AfterWarmifySideEffects();

        ResumeGameIfPausedForLever();
    }

    //==================================================
    // 暖色化（色＆明るさを徐々に変える）
    //==================================================

    /*
         暖色フェード
         ・開始時点の色/明るさを出発点とする
         ・最終カラーは WarmLightColor
         ・最終明るさは baseline × FinalIntensityPercent%
         ・TimeScale=0でも unscaledDeltaTime で進む
    */
    private IEnumerator CoWarmifyLightsGradually()
    {
        CaptureBaselinesIfNeeded();

        // 出発点（現在値）を保存
        var startColors = new List<Color>(LightLists.Count);
        var startIntensities = new List<float>(LightLists.Count);
        for (int i = 0; i < LightLists.Count; i++)
        {
            var l = LightLists[i];
            if (!l)
            {
                startColors.Add(Color.white);
                startIntensities.Add(0f);
                continue;
            }
            startColors.Add(l.color);
            startIntensities.Add(l.intensity);
        }

        float percent = Mathf.Max(0f, FinalIntensityPercent) * 0.01f;

        float dur = Mathf.Max(0.01f, WarmifyLerpDuration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float eased = WarmifyCurve != null ? WarmifyCurve.Evaluate(u) : u;

            for (int i = 0; i < LightLists.Count; i++)
            {
                var l = LightLists[i];
                if (!l) continue;

                // 色を補間
                Color fromC = startColors[i];
                Color toC = WarmLightColor;
                l.color = Color.Lerp(fromC, toC, eased);

                // intensityを補間（baseline × percent に向ける）
                float baseI = (_baselineIntensities != null && i < _baselineIntensities.Count)
                                ? _baselineIntensities[i]
                                : startIntensities[i];
                float targetI = baseI * percent;

                l.intensity = Mathf.Lerp(startIntensities[i], targetI, eased);
            }

            yield return null;
        }

        // 最終値で固定
        for (int i = 0; i < LightLists.Count; i++)
        {
            var l = LightLists[i];
            if (!l) continue;

            float baseI = (_baselineIntensities != null && i < _baselineIntensities.Count)
                            ? _baselineIntensities[i]
                            : startIntensities[i];

            l.color = WarmLightColor;
            l.intensity = baseI * percent;
        }

        OnLight = false;
        Debug.Log($"[LightOff] Warmify done. Final = baseline × {FinalIntensityPercent:F1}%");
    }

    //==================================================
    // 暖色化後の副作用（演出・進行・破棄）
    //==================================================

    /*
         暖色化が終わった後に起きること
         ・ゴースト破棄 / 呪いエフェクト破棄
         ・メッセージ表示
         ・Hint進行 + Tutorial経由でドア反映
         ・鍵開きSE
    */
    private void AfterWarmifySideEffects()
    {
        if (Ghost) Destroy(Ghost.gameObject);

        if (CurseEffects != null)
        {
            foreach (var eff in CurseEffects)
            {
                if (!eff) continue;
                Destroy(eff);
            }
        }

        ShowEventMessage(MsgTurnedOff);

        var hint = GetOrResolveHintRef();
        if (hint)
        {
            if (!CountOnlyOncePerThisLight || (CountOnlyOncePerThisLight && !_alreadyCounted))
            {
                int before = hint.ProgressStage;

                for (int i = 0; i < Mathf.Max(1, AdvanceAmountOnOff); i++)
                    hint.AdvanceProgress();

                // 動かなかったら強制Set（保険）
                if (hint.ProgressStage == before)
                    hint.SetProgress(before + Mathf.Max(1, AdvanceAmountOnOff));

                int after = hint.ProgressStage;
                Debug.Log($"[LightOff] Warmify progress: {before} -> {after} (HintRef={hint.GetInstanceID()})");

                // ドアを開ける反映（Tutorial経由）
                var tut = GetTutorial();
                if (tut)
                {
                    if (after <= before)
                    {
                        Debug.LogWarning("[LightOff] Progress didn’t move. Forcing unlock as fallback.");
                        tut.ForceUnlockDoors();
                    }
                    tut.ReapplyDoorByCurrentProgress();
                }

                PlayUnlockSound();

                _alreadyCounted = true;
            }
        }
        else
        {
            Debug.LogWarning("[LightOff] HintRef が見つからないのでドアだけ無理やり進めます。");
            var tut = GetTutorial();
            if (tut)
            {
                tut.ForceUnlockDoors();
                tut.ReapplyDoorByCurrentProgress();
                PlayUnlockSound();
            }
        }
    }

    //==================================================
    // 逆方向：ライトを明るい側(冷色)に戻す
    //==================================================

    /*
         暖色→冷色の復帰
         ・intensityを baseline に戻す（色は現状維持）
         ・必要ならここで Color.white などに戻す処理を追加できる（コメントに記載）
         ・戻した後はロックして再操作不可にする（仕様）
    */
    private void TurnOnCold()
    {
        if (IsLocked()) return;
        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        CaptureBaselinesIfNeeded();

        for (int i = 0; i < LightLists.Count; i++)
        {
            var l = LightLists[i];
            if (!l) continue;

            float baseI = (_baselineIntensities != null && i < _baselineIntensities.Count)
                            ? _baselineIntensities[i]
                            : l.intensity;
            l.intensity = baseI;

            // 必要なら「冷たい白」に戻したい場合はここでやる
            // 例: l.color = Color.white;
        }

        OnLight = true;
        Debug.Log("ライトを点けた(明るい側に戻した)");
        ShowEventMessage(MsgTurnedOn);

        // 進行度を下げる（任意仕様）
        var hint = GetOrResolveHintRef();
        if (hint && DecreaseAmountOnOn > 0)
        {
            int before = hint.ProgressStage;
            hint.SetProgress(before - DecreaseAmountOnOn);
            int after = hint.ProgressStage;
            Debug.Log($"[LightOff] Turn ON: stage {before} -> {after} (HintRef={hint.GetInstanceID()})");

            var tut = GetTutorial();
            if (tut) tut.ReapplyDoorByCurrentProgress();
        }

        // 明るい側に戻したら、以降は触らせない仕様
        _lockedOn = true;
        if (PromptText) PromptText.gameObject.SetActive(false);
    }

    //==================================================
    // カメラ切り替え
    //==================================================

    // 見せカメラに切り替える（MainCameraが未設定ならCamera.mainを拾う）
    private void SwitchToShowcaseCamera()
    {
        if (!MainCamera && Camera.main) MainCamera = Camera.main;

        if (ShowcaseCamera)
        {
            if (MainCamera) MainCamera.enabled = false;
            ShowcaseCamera.enabled = true;
        }
    }

    // 通常カメラに戻す
    private void SwitchBackToMainCamera()
    {
        if (ShowcaseCamera) ShowcaseCamera.enabled = false;
        if (!MainCamera && Camera.main) MainCamera = Camera.main;
        if (MainCamera) MainCamera.enabled = true;
    }

    //==================================================
    // メッセージ表示
    //==================================================

    // イベントメッセージを一定時間表示する
    private void ShowEventMessage(string msg)
    {
        if (!MsgText) return;
        MsgText.text = msg;
        MsgText.gameObject.SetActive(true);
        _msgTimer = Mathf.Max(0.01f, EventMsgDuration);
    }
}