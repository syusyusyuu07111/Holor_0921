using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class LightOff : MonoBehaviour
{
    public GameObject Player;                               // プレイヤー
    public GameObject Light;                                // 近接判定の位置（スイッチ等）
    public float PushDistance = 3.0f;                       // インタラクト距離
    public bool OnLight = true;                             // 今ライトが点いているか
    public GameObject Ghost;                                // 暖色化時に消す対象（任意）
    public GameObject lever;                                // 回すレバー
    public float RotateLever = 30f;                         // 回す量（X度）

    [Header("レバー回転（追加）")]
    public float LeverRotateSpeed = 180f;                   // 回転速度[deg/sec]
    private bool _isLeverAnimating = false;                 // 回転中フラグ

    [SerializeField] private List<Light> LightLists = new();// 操作対象ライト群

    // ==== 暖色設定（Inspector） ====
    [Header("ライト暖色（OFF操作時に適用）")]
    public Color WarmLightColor = new Color(1.0f, 0.78f, 0.56f, 1f); // デフォ“暖かい色”

    // ==== 見せカメラ切替 ====
    [Header("見せ用カメラ（任意）")]
    public Camera MainCamera;                               // 通常表示カメラ（未設定なら Camera.main）
    public Camera ShowcaseCamera;                           // 見せ用の固定/演出カメラ
    public float ShowcaseHoldSeconds = 0.5f;                // 色変更後に“見せる”実時間

    InputSystem_Actions input;                              // 新InputSystem

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
    public float EventMsgDuration = 5.0f;                   // メッセージ表示秒数
    private float _msgTimer = 0f;

    // ------- 進行度連携 -------
    [Header("進行度（ミッション）")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int AdvanceAmountOnOff = 1;                      // 暖色化で進む
    public int DecreaseAmountOnOn = 1;                      // 点灯で下がる

    [Tooltip("同じライトでは最初の“暖色化”だけを進行度にカウントする")]
    public bool CountOnlyOncePerThisLight = false;
    private bool _alreadyCounted = false;

    [Tooltip("トグルの連打での多重カウント防止（秒）")]
    public float ToggleDebounceSeconds = 0.25f;
    private float _lastToggleTime = -999f;

    // ------- “一度点けたら固定ON”ロック -------
    private bool _lockedOn = false;
    private bool IsLocked() => _lockedOn;

    // ------- レバー中はゲーム時間停止 -------
    private bool _pausedForLever = false;
    private float _timeScaleBeforePause = 1f;
    private void PauseGameForLever()
    {
        if (_pausedForLever) return;
        _timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;                                // ★時間停止
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
        if (AutoFindHintRef && !HintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = UnityEngine.Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = FindObjectOfType<HintText>(true);
#endif
        }

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

    void Update()
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

        // 入力
        if (inRange && !_isLeverAnimating && !IsLocked() && input.Player.Jump.triggered) // Jump=インタラクト
        {
            if (OnLight) Off(); else On();
        }

        // メッセージ寿命
        if (_msgTimer > 0f)
        {
            _msgTimer -= Time.deltaTime;                    // timeScaleの影響を受ける
            if (_msgTimer <= 0f && MsgText)
            {
                MsgText.text = "";
                MsgText.gameObject.SetActive(false);
            }
        }
    }

    // ========= “OFF操作”＝暖色にする + カメラ演出 =========
    void Off()
    {
        if (IsLocked()) return;

        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        // 順番：押す＞時間止める＞レバー＞カメラ切替＞色変える＞数秒待つ＞カメラ戻す＞時間戻す
        if (lever && RotateLever != 0f)
        {
            if (!_isLeverAnimating) StartCoroutine(CoRotateLeverThenShowcaseThenWarmify());
            return;
        }

        // レバー無し：カメラ切替＞色＞待つ＞戻す＞時間戻す
        StartCoroutine(CoOnlyShowcaseThenWarmify());
    }

    // レバーあり：押す＞時間止める＞レバー＞カメラ切替＞色＞待つ＞カメラ戻す＞時間戻す
    private System.Collections.IEnumerator CoRotateLeverThenShowcaseThenWarmify()
    {
        _isLeverAnimating = true;
        PauseGameForLever();                                // ★時間停止

        // レバー回転（Xのみ、停止中でも進む）
        Transform tf = lever.transform;
        Vector3 euler = tf.localEulerAngles;
        float startX = euler.x;
        float endX = startX + RotateLever;
        float duration = Mathf.Max(0.01f, Mathf.Abs(RotateLever) / Mathf.Max(1f, LeverRotateSpeed));
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;                    // 停止中でも進行
            float x = Mathf.LerpAngle(startX, endX, Mathf.Clamp01(t / duration));
            euler = tf.localEulerAngles; euler.x = x;
            tf.localEulerAngles = euler;
            yield return null;
        }
        euler = tf.localEulerAngles; euler.x = endX; tf.localEulerAngles = euler;

        // カメラ切替（メイン→見せ）
        SwitchToShowcaseCamera();

        // 直ちに色変更
        DoWarmifyInternal();

        // “見せ”のための待機（実時間）
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, ShowcaseHoldSeconds));

        // カメラ戻す（見せ→メイン）
        SwitchBackToMainCamera();

        _isLeverAnimating = false;

        // 最後に時間再開
        ResumeGameIfPausedForLever();
    }

    // レバー無し：カメラ切替＞色＞待つ＞戻す＞時間戻す
    private System.Collections.IEnumerator CoOnlyShowcaseThenWarmify()
    {
        PauseGameForLever();                                // ★時間停止

        SwitchToShowcaseCamera();
        DoWarmifyInternal();
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, ShowcaseHoldSeconds));
        SwitchBackToMainCamera();

        ResumeGameIfPausedForLever();                       // ★時間再開
    }

    // 暖色化（enabledは触らず色のみ）
    private void DoWarmifyInternal()
    {
        foreach (var l in LightLists)
        {
            if (!l) continue;
            l.color = WarmLightColor;                       // ★ 暖かい色を適用
        }

        OnLight = false;                                    // UIテキスト上の“OFF側”扱い
        Debug.Log("ライトを暖色にした");

        if (Ghost) Destroy(Ghost.gameObject);               // 任意：暖色化時にゴースト破棄

        ShowEventMessage(MsgTurnedOff);

        // 進行度（必要なら一回きり）
        if (!CountOnlyOncePerThisLight || (CountOnlyOncePerThisLight && !_alreadyCounted))
        {
            if (HintRef && AdvanceAmountOnOff > 0)
            {
                for (int i = 0; i < AdvanceAmountOnOff; i++)
                    HintRef.AdvanceProgress();
            }
            _alreadyCounted = true;
        }
    }

    // ========= 全ライトON：進行度－＆固定ONロック =========
    void On()
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

        if (HintRef && DecreaseAmountOnOn > 0)
        {
            for (int i = 0; i < DecreaseAmountOnOn; i++)
                HintRef.SetProgress(HintRef.ProgressStage - 1);
        }

        _lockedOn = true;                                   // 以降は無反応
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
    void ShowEventMessage(string msg)
    {
        if (!MsgText) return;
        MsgText.text = msg;
        MsgText.gameObject.SetActive(true);
        _msgTimer = Mathf.Max(0.01f, EventMsgDuration);     // timeScaleの影響を受ける
    }
}
