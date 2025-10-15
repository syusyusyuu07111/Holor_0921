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
    public GameObject Ghost;                                // 消灯時に消す対象（任意）

    [SerializeField] private List<Light> LightLists = new();// 操作対象ライト群

    InputSystem_Actions input;                              // 新InputSystem

    // --------------- ここから実装を追加（2種類のテキスト） ---------------
    public TextMeshProUGUI PromptText;                      // 近づいた時だけ出す「キー案内」
    public TextMeshProUGUI MsgText;                         // 点いた/消えた“瞬間だけ”出るメッセージ
    // --------------- ここまで実装を追加 ---------------

    // --------------- ここから実装を追加（文言/表示時間） ---------------
    [Header("文言設定")]
    public string PromptOn = "【E】ライトを消す";           // 点灯中に近づいた時の案内
    public string PromptOff = "【E】ライトを点ける";         // 消灯中に近づいた時の案内
    public string MsgTurnedOff = "ライトが消えたようだ";     // 消した直後のメッセージ
    public string MsgTurnedOn = "ライトが点いたようだ";     // 点けた直後のメッセージ

    [Header("表示時間")]
    public float EventMsgDuration = 5.0f;                   // メッセージ表示秒数
    private float _msgTimer = 0f;                           // 残り表示時間
    // --------------- ここまで実装を追加 ---------------

    // --------------- ここから実装を追加（進行度連携） ---------------
    [Header("進行度（ミッション）")]
    public HintText HintRef;                                // ヒント/進行度管理への参照（任意）
    public bool AutoFindHintRef = true;                     // 未設定なら自動検索
    public int AdvanceAmountOnOff = 1;                      // 消灯で進む段数（通常1）
    public int DecreaseAmountOnOn = 1;                      // 点灯で下がる段数（通常1）

    [Tooltip("同じライトでは最初の消灯だけを進行度にカウントする")]
    public bool CountOnlyOncePerThisLight = false;          // 一回きりにするか？
    private bool _alreadyCounted = false;                   // このライトで既に加算したか

    [Tooltip("トグルの連打での多重カウント防止（秒）")]
    public float ToggleDebounceSeconds = 0.25f;             // デバウンス秒（消灯/点灯の両方で使用）
    private float _lastToggleTime = -999f;                   // 直近トグル時刻
    // --------------- ここまで実装を追加 ---------------

    private void Awake()
    {
        input = new InputSystem_Actions();                  // 入力クラス生成
        // --------------- ここから実装を追加（進行度の自動取得：非推奨置換対応） ---------------
        if (AutoFindHintRef && !HintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = UnityEngine.Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = FindObjectOfType<HintText>(true);
#endif
        }
        // --------------- ここまで実装を追加 ---------------
    }

    private void OnEnable()
    {
        input.Player.Enable();                              // アクション有効化
        // --------------- ここから実装を追加（初期は非表示） ---------------
        if (PromptText) { PromptText.text = ""; PromptText.gameObject.SetActive(false); }
        if (MsgText) { MsgText.text = ""; MsgText.gameObject.SetActive(false); }
        // --------------- ここまで実装を追加 ---------------
    }

    private void OnDisable()
    {
        input.Player.Disable();                             // アクション無効化
    }

    void Update()
    {
        if (!Player || !Light) return;                      // 参照欠けガード

        // 近接チェック ---------------------------------------------------------------
        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        bool inRange = (distance < PushDistance);

        // キー案内（近接時のみ） -----------------------------------------------------
        if (PromptText) PromptText.gameObject.SetActive(inRange);
        if (inRange && PromptText)
        {
            PromptText.text = OnLight ? PromptOn : PromptOff;
        }

        // 入力トグル ----------------------------------------------------------------
        if (inRange && input.Player.Jump.triggered)         // ← Jump をインタラクトに使用中
        {
            if (OnLight) Off(); else On();
        }

        // メッセージ寿命 -------------------------------------------------------------
        if (_msgTimer > 0f)
        {
            _msgTimer -= Time.deltaTime;
            if (_msgTimer <= 0f && MsgText)
            {
                MsgText.text = "";
                MsgText.gameObject.SetActive(false);        // 5秒経ったら消す
            }
        }
    }

    // --------------- ここから実装を追加（全ライトOFF：進行度＋） ---------------
    void Off()
    {
        // デバウンス（消灯/点灯共通） ------------------------------------------------
        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        foreach (var l in LightLists)
        {
            if (l) l.enabled = false;
        }
        OnLight = false;
        Debug.Log("ライトを消した");

        if (Ghost) Destroy(Ghost.gameObject);               // 仕様：消灯時にゴースト破棄（任意）

        ShowEventMessage(MsgTurnedOff);                     // 「消えたようだ」

        // 進行度を進める（一回きりモード考慮）
        if (!CountOnlyOncePerThisLight || (CountOnlyOncePerThisLight && !_alreadyCounted))
        {
            if (HintRef && AdvanceAmountOnOff > 0)
            {
                for (int i = 0; i < AdvanceAmountOnOff; i++)
                    HintRef.AdvanceProgress();              // 進行度 +1（複数可）
            }
            _alreadyCounted = true;                         // 一回きりフラグ
        }
    }
    // --------------- ここまで実装を追加 ---------------

    // --------------- ここから実装を追加（全ライトON：進行度－） ---------------
    void On()
    {
        // デバウンス（消灯/点灯共通） ------------------------------------------------
        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        foreach (var l in LightLists)
        {
            if (l) l.enabled = true;
        }
        OnLight = true;
        Debug.Log("ライトを点けた");

        ShowEventMessage(MsgTurnedOn);                      // 「点いたようだ」

        // --------------- ここから実装を追加（点灯で進行度を下げる） ---------------
        // 条件：直前が「消えていた」状態 → 今点けたので減少させる
        if (HintRef && DecreaseAmountOnOn > 0)
        {
            for (int i = 0; i < DecreaseAmountOnOn; i++)
            {
                // SetProgress で下限クランプされる前提。直接1段ずつ下げる。
                HintRef.SetProgress(HintRef.ProgressStage - 1);
            }
        }
        // ※ 一回きり制御は「減少」には適用しない（仕様）。必要なら同様のフラグを追加してね。
        // --------------- ここまで実装を追加 ---------------
    }
    // --------------- ここまで実装を追加 ---------------

    // --------------- ここから実装を追加（メッセージ表示共通） ---------------
    void ShowEventMessage(string msg)
    {
        if (!MsgText) return;
        MsgText.text = msg;
        MsgText.gameObject.SetActive(true);
        _msgTimer = Mathf.Max(0.01f, EventMsgDuration);     // 表示時間リセット
    }
    // --------------- ここまで実装を追加 ---------------
}
