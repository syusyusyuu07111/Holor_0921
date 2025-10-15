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
    public TextMeshProUGUI MsgText;                         // 点いた/消えた“瞬間だけ”出すメッセージ
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

    private void Awake()
    {
        input = new InputSystem_Actions();                  // 入力クラス生成
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

        // 距離チェック ---------------------------------------------------------------
        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        bool inRange = (distance < PushDistance);

        // 近づいたらキー案内を表示／離れたら非表示 -----------------------------------
        if (PromptText) PromptText.gameObject.SetActive(inRange);
        if (inRange && PromptText)
        {
            PromptText.text = OnLight ? PromptOn : PromptOff;
        }

        // 入力でトグル ---------------------------------------------------------------
        if (inRange && input.Player.Jump.triggered)        // ← Jumpをインタラクトに使用中
        {
            if (OnLight) Off(); else On();
        }

        // メッセージ寿命管理 ---------------------------------------------------------
        if (_msgTimer > 0f)
        {
            _msgTimer -= Time.deltaTime;
            if (_msgTimer <= 0f && MsgText)
            {
                MsgText.text = "";
                MsgText.gameObject.SetActive(false);       // 5秒経ったら消す
            }
        }
    }

    // --------------- ここから実装を追加（全ライトOFF） ---------------
    void Off()
    {
        foreach (var l in LightLists)
        {
            if (l) l.enabled = false;
        }
        OnLight = false;
        Debug.Log("ライトを消した");

        if (Ghost) Destroy(Ghost.gameObject);              // 仕様：消灯時にゴースト破棄（任意）

        ShowEventMessage(MsgTurnedOff);                    // 「消えたようだ」
    }
    // --------------- ここまで実装を追加 ---------------

    // --------------- ここから実装を追加（全ライトON） ---------------
    void On()
    {
        foreach (var l in LightLists)
        {
            if (l) l.enabled = true;
        }
        OnLight = true;
        Debug.Log("ライトを点けた");

        ShowEventMessage(MsgTurnedOn);                     // 「点いたようだ」
    }
    // --------------- ここまで実装を追加 ---------------

    // --------------- ここから実装を追加（メッセージ表示共通） ---------------
    void ShowEventMessage(string msg)
    {
        if (!MsgText) return;
        MsgText.text = msg;
        MsgText.gameObject.SetActive(true);
        _msgTimer = Mathf.Max(0.01f, EventMsgDuration);    // 表示時間リセット
    }
    // --------------- ここまで実装を追加 ---------------
}
