using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class LightOff : MonoBehaviour
{
    public GameObject Player;                               // プレイヤー
    public GameObject Light;                                // 近接判定の位置（スイッチ等）
    public float PushDistance = 3.0f;                       // インタラクト距離
    public bool OnLight = true;                             // 今ライトが点いているか（UI表示用）
    public GameObject Ghost;                                // 暖色化時に消す対象（任意）
    public GameObject lever;                                // 回すレバー
    public float RotateLever = 30f;                         // 回す量（X度）

    [Header("レバー回転（追加）")]
    public float LeverRotateSpeed = 180f;                   // 回転速度[deg/sec]
    private bool _isLeverAnimating = false;                 // 回転中フラグ

    [SerializeField] private List<Light> LightLists = new();// 操作対象ライト群

    // ==== 暖色設定 ====
    [Header("ライト暖色（OFF操作時に適用）")]
    public Color WarmLightColor = new Color(1.0f, 0.78f, 0.56f, 1f);

    // ==== 見せカメラ切替 ====
    [Header("見せ用カメラ（任意）")]
    public Camera MainCamera;                               // 通常表示カメラ（未設定なら Camera.main）
    public Camera ShowcaseCamera;                           // 見せ用の固定/演出カメラ
    public float ShowcaseHoldSeconds = 0.5f;                // 色変更後に“見せる”実時間（実時間）

    private InputSystem_Actions input;                      // 新InputSystem

    // ------- 2種類のテキスト -------
    public TextMeshProUGUI PromptText;                      // 近づいた時だけ出す「キー案内」
    public TextMeshProUGUI MsgText;                         // 点いた/消えた“瞬間だけ”出るメッセージ

    // ------- 文言/表示時間 -------
    [Header("文言設定")]
    public string PromptOn = "【E】暖色にする";              // 点灯中：暖色にする
    public string PromptOff = "【E】ライトを点ける";         // 消灯中の案内
    public string MsgTurnedOff = "ライトが暖かい色になった";
    public string MsgTurnedOn = "ライトが点いたようだ";

    [Header("表示時間")]
    public float EventMsgDuration = 5.0f;                   // メッセージ表示秒数（ゲーム時間）
    private float _msgTimer = 0f;

    // ------- 進行度連携 -------
    [Header("進行度（ミッション）")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;

    [Tooltip("暖色化で進む量（>=1 推奨）")]
    public int AdvanceAmountOnOff = 1;

    [Tooltip("点灯で下げる量（>=1 なら減る）")]
    public int DecreaseAmountOnOn = 0;

    [Tooltip("同じライトでは最初の“暖色化”だけを進行度にカウントする")]
    public bool CountOnlyOncePerThisLight = false;
    private bool _alreadyCounted = false;

    [Tooltip("トグルの連打での多重カウント防止（秒）")]
    public float ToggleDebounceSeconds = 0.25f;
    private float _lastToggleTime = -999f;

    // ------- “一度点けたら固定ON”ロック（例：戻せない演出にしたい時） -------
    private bool _lockedOn = false;
    private bool IsLocked() => _lockedOn;

    // ------- レバー中はゲーム時間停止 -------
    private bool _pausedForLever = false;
    private float _timeScaleBeforePause = 1f;

    // ===== Tutorial 参照キャッシュ（Find の旧API警告対策込み） =====
    private Tutorial _cachedTutorial = null;
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

    // ===== HintRef を安全に取得（未設定なら Tutorial から拝借） =====
    private HintText GetOrResolveHintRef()
    {
        if (HintRef) return HintRef;

        var tut = GetTutorial();
        if (tut && tut.HintRef)
        {
            HintRef = tut.HintRef; // 参照揃える
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

    private void PauseGameForLever()
    {
        if (_pausedForLever) return;
        _timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;                                // ★時間停止（実時間コルーチン使用）
        _pausedForLever = true;
    }
    private void ResumeGameIfPausedForLever()
    {
        if (!_pausedForLever) return;
        Time.timeScale = _timeScaleBeforePause;             // ★時間再開
        _pausedForLever = false;
    }

    private void Awake()
    {
        input = new InputSystem_Actions();

        if (!MainCamera && Camera.main) MainCamera = Camera.main;
        if (ShowcaseCamera) ShowcaseCamera.enabled = false; // 初期は無効
    }

    private void OnEnable()
    {
        input.Player.Enable();
        if (PromptText) { PromptText.text = ""; PromptText.gameObject.SetActive(false); }
        if (MsgText) { MsgText.text = ""; MsgText.gameObject.SetActive(false); }
    }

    private void OnDisable()
    {
        input.Player.Disable();
    }

    private void OnDestroy()
    {
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

        // 近接チェック
        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        bool inRange = (distance < PushDistance);

        // 案内表示（ロック/レバー中は非表示）
        if (PromptText) PromptText.gameObject.SetActive(inRange && !_isLeverAnimating && !IsLocked());
        if (inRange && PromptText && !IsLocked())
        {
            PromptText.text = OnLight ? PromptOn : PromptOff;
        }

        // 入力（Jump=インタラクト）
        if (inRange && !_isLeverAnimating && !IsLocked() && input.Player.Jump.triggered)
        {
            if (OnLight) TurnOffToWarm(); else TurnOnCold();
        }

        // メッセージ寿命
        if (_msgTimer > 0f)
        {
            _msgTimer -= Time.deltaTime; // timeScaleの影響を受ける
            if (_msgTimer <= 0f && MsgText)
            {
                MsgText.text = "";
                MsgText.gameObject.SetActive(false);
            }
        }
    }

    // ========= OFF操作：暖色にする + カメラ演出 =========
    private void TurnOffToWarm()
    {
        if (IsLocked()) return;
        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        if (lever && RotateLever != 0f)
        {
            if (!_isLeverAnimating) StartCoroutine(CoRotateLeverThenShowcaseThenWarmify());
        }
        else
        {
            StartCoroutine(CoOnlyShowcaseThenWarmify());
        }
    }

    // レバーあり：押す＞時間止める＞レバー＞カメラ切替＞色＞待機＞戻す＞時間戻す
    private System.Collections.IEnumerator CoRotateLeverThenShowcaseThenWarmify()
    {
        _isLeverAnimating = true;
        PauseGameForLever();

        // レバー回転
        Transform tf = lever.transform;
        Vector3 euler = tf.localEulerAngles;
        float startX = euler.x;
        float endX = startX + RotateLever;
        float duration = Mathf.Max(0.01f, Mathf.Abs(RotateLever) / Mathf.Max(1f, LeverRotateSpeed));
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // 停止中でも進行
            float x = Mathf.LerpAngle(startX, endX, Mathf.Clamp01(t / duration));
            euler = tf.localEulerAngles; euler.x = x;
            tf.localEulerAngles = euler;
            yield return null;
        }
        euler = tf.localEulerAngles; euler.x = endX; tf.localEulerAngles = euler;

        // カメラ切替→暖色→“見せ”→カメラ戻す
        SwitchToShowcaseCamera();
        DoWarmifyInternal();
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, ShowcaseHoldSeconds));
        SwitchBackToMainCamera();

        _isLeverAnimating = false;
        ResumeGameIfPausedForLever();
    }

    // レバー無し：カメラ切替＞色＞待機＞戻す＞時間戻す
    private System.Collections.IEnumerator CoOnlyShowcaseThenWarmify()
    {
        PauseGameForLever();

        SwitchToShowcaseCamera();
        DoWarmifyInternal();
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, ShowcaseHoldSeconds));
        SwitchBackToMainCamera();

        ResumeGameIfPausedForLever();
    }

    // ====== 暖色化（ここで“必ず進行度が上がる”） ======
    private void DoWarmifyInternal()
    {
        // ライト色を暖色へ
        foreach (var l in LightLists)
        {
            if (!l) continue;
            l.color = WarmLightColor;
        }

        OnLight = false; // UI上は“OFF→暖色化”側
        Debug.Log("ライトを暖色にした");

        if (Ghost) Destroy(Ghost.gameObject); // 任意：暖色化でゴースト退場
        ShowEventMessage(MsgTurnedOff);

        // ---- 進行度：絶対に上げる（多段フォールバック） ----
        var hint = GetOrResolveHintRef();
        if (hint)
        {
            // 一回きりオプション
            if (!CountOnlyOncePerThisLight || (CountOnlyOncePerThisLight && !_alreadyCounted))
            {
                int before = hint.ProgressStage;

                // 1) 通常ルート：AdvanceProgress()
                for (int i = 0; i < Mathf.Max(1, AdvanceAmountOnOff); i++)
                    hint.AdvanceProgress();

                // 2) 変化がなければ、強制 SetProgress
                if (hint.ProgressStage == before)
                {
                    hint.SetProgress(before + Mathf.Max(1, AdvanceAmountOnOff));
                }

                int after = hint.ProgressStage;
                Debug.Log($"[LightOff] Warmify (guaranteed): stage {before} -> {after} (HintRef={hint.GetInstanceID()})");

                // 3) それでも 0 のままなら、Tutorial に強制解錠を依頼（最終保険）
                var tut = GetTutorial();
                if (tut)
                {
                    if (after <= before)
                    {
                        Debug.LogWarning("[LightOff] Progress didn’t move. Forcing unlock as fallback.");
                        tut.ForceUnlockDoors();
                    }
                    // ドア側へ即時反映（保険）
                    tut.ReapplyDoorByCurrentProgress();
                }

                _alreadyCounted = true;
            }
        }
        else
        {
            Debug.LogWarning("[LightOff] HintRef を見つけられませんでした。Tutorial 側の ForceUnlock を呼びます。");
            var tut = GetTutorial();
            if (tut)
            {
                tut.ForceUnlockDoors();
                tut.ReapplyDoorByCurrentProgress();
            }
        }
    }

    // ========= 全ライトON：進行度を下げる（任意） =========
    private void TurnOnCold()
    {
        if (IsLocked()) return;
        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        foreach (var l in LightLists)
        {
            if (l) l.enabled = true;
        }

        OnLight = true;
        Debug.Log("ライトを点けた");
        ShowEventMessage(MsgTurnedOn);

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

        // “一度点けたら固定ON”にするなら true に
        _lockedOn = true;
        if (PromptText) PromptText.gameObject.SetActive(false);
    }

    // ========= カメラ切りかえ =========
    private void SwitchToShowcaseCamera()
    {
        if (!MainCamera && Camera.main) MainCamera = Camera.main;

        if (ShowcaseCamera)
        {
            if (MainCamera) MainCamera.enabled = false;
            ShowcaseCamera.enabled = true;
        }
    }

    private void SwitchBackToMainCamera()
    {
        if (ShowcaseCamera) ShowcaseCamera.enabled = false;
        if (!MainCamera && Camera.main) MainCamera = Camera.main;
        if (MainCamera) MainCamera.enabled = true;
    }

    // ========= メッセージ表示共通 =========
    private void ShowEventMessage(string msg)
    {
        if (!MsgText) return;
        MsgText.text = msg;
        MsgText.gameObject.SetActive(true);
        _msgTimer = Mathf.Max(0.01f, EventMsgDuration);     // timeScale の影響を受ける
    }
}
