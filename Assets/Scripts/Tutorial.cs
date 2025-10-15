using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ===== テキスト／タイプ演出 =====
    [Header("共通UI")]
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;            // 行間の待機
    public bool HideWhenDone = true;             // 全文後に自動で非表示

    [TextArea]
    public string[] Step1Lines = {
        "……ここはどこだろう。", "さっきまでの記憶が曖昧だ。", "とにかく、出口を探さないと。"
    };

    [Header("Step2：ロック中ドアを叩いた時のメッセージ")]
    public string DoorLockedMessage = "ドアはあかないようだ…";
    public float DoorLockedCooldown = 1.2f;      // 連打ガード
    public float AfterDoorMsgDelay = 2.0f;       // Step2のテキストが消えた“後”の追加待機

    [Header("Step3：出現後のメッセージ")]
    [TextArea]
    public string[] Step3Lines = {
        "……何か音がしたぞ！", "周りを探してみよう。"
    };

    Coroutine _typing;                           // 走っているタイプコルーチン
    float _doorMsgCD = 0f;                       // 連打ガード

    // ===== 進行度参照 =====
    [Header("進行度参照")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;      // これ未満では OpenDoor を無効化

    // ===== OpenDoor 制御（スクリプトだけ有効/無効） =====
    [Header("OpenDoor（コンポーネントのみ切替）")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue;

    // ===== 入力とドア叩き検知 =====
    [Header("ドア入力検知")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;

    private InputSystem_Actions _input;

    // ===== スポナー（チュートリアルから発火） =====
    [Header("敵スポナー制御")]
    public EnemyAI Spawner;                       // AutoStart=false にしておく
    public bool StartSpawnLoopAfterFirst = true;  // 最初の確定湧き後に抽選開始する

    // ===== Step4：初めて幽霊が画面に映ったら画像を表示（任意） =====
    [Header("Step4（初見チュートリアル画像）")]
    public GameObject Step4Panel;                 // 表示したいパネル（任意）
    public bool Step4AutoHide = true;
    public float Step4VisibleSeconds = 3f;
    private bool _didStep4 = false;
    private Coroutine _step4Co;

    // ===== 内部状態 =====
    private bool _didStep2Flow = false;           // Step2→Step3 の一連は一度だけ

    private void Awake()
    {
        if (!HintRef && AutoFindHintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = FindObjectOfType<HintText>(true);
#endif
        }
        _input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _input.Player.Enable();

        // 進行度イベント
        if (HintRef) HintRef.OnProgressChanged.AddListener(OnProgressChanged);

        // Step4：初めてカメラに映った合図（HintText 側にイベントがある場合）
        if (HintRef && HintRef.OnFirstGhostSeen != null)
            HintRef.OnFirstGhostSeen.AddListener(Step4);
    }

    private void OnDisable()
    {
        if (HintRef) HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        if (HintRef && HintRef.OnFirstGhostSeen != null)
            HintRef.OnFirstGhostSeen.RemoveListener(Step4);
        _input.Player.Disable();
    }

    private void Start()
    {
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }
        if (Step4Panel) Step4Panel.SetActive(false);

        // 進行度に応じてドアのコンポーネントを切替
        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);

        // Step1 開始
        Step1();
    }

    private void Update()
    {
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        HandleLockedDoorTapFeedback();   // Step2 の入口
        if (_doorMsgCD > 0f) _doorMsgCD -= Time.deltaTime;
    }

    // =========================================================
    // Step1
    // =========================================================
    public void Step1()
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines));
    }

    // =========================================================
    // Step2：ロック中ドアを叩いた瞬間に表示 → 消えたのを待って 2 秒後にスポーン確定
    // =========================================================
    void HandleLockedDoorTapFeedback()
    {
        if (!Player) return;
        if (_didStep2Flow) return; // 一度だけでOK
        if (_doorMsgCD > 0f) return;

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();

        if (!pressed) return;

        // 近い & OpenDoor が無効なドアがあるか
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            if (od.enabled) continue; // 開けられる状態ならチュートリアルトリガーじゃない

            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance)
                continue;

            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // ここで Step2 を開始
            _didStep2Flow = true;
            StartCoroutine(CoStep2ThenSpawn());  // メッセージ→消えるまで待つ→2秒→スポーン
            break;
        }
    }

    IEnumerator CoStep2ThenSpawn()
    {
        // 「ドアはあかないようだ…」をワンショットで
        yield return StartCoroutine(CoTypeOneShot(DoorLockedMessage));

        // “消えた後”さらに待機
        yield return new WaitForSeconds(AfterDoorMsgDelay);

        // スポーン確定（抽選スキップ）
        if (Spawner) Spawner.ForceSpawnOnce();

        // Step3 テキスト
        if (Step3Lines != null && Step3Lines.Length > 0)
        {
            if (_typing != null) StopCoroutine(_typing);
            BottomText.gameObject.SetActive(true);
            _typing = StartCoroutine(CoTypeLines(Step3Lines));
        }

        // 以降は抽選ループ開始（任意）
        if (Spawner && StartSpawnLoopAfterFirst) Spawner.BeginSpawning();
    }

    // =========================================================
    // Step4：初めて幽霊が画面に映ったら画像を出す（HintText 側のイベントを利用）
    // =========================================================
    public void Step4()
    {
        if (_didStep4) return;
        _didStep4 = true;
        if (_step4Co != null) StopCoroutine(_step4Co);
        _step4Co = StartCoroutine(CoStep4Show());
    }

    IEnumerator CoStep4Show()
    {
        if (Step4Panel) Step4Panel.SetActive(true);

        if (Step4AutoHide && Step4VisibleSeconds > 0f)
        {
            yield return new WaitForSeconds(Step4VisibleSeconds);
            if (Step4Panel) Step4Panel.SetActive(false);
        }
        _step4Co = null;
    }

    // =========================================================
    // 進行度に応じた OpenDoor コンポーネントの有効化
    // =========================================================
    void ApplyDoorEnableByProgress(int progress)
    {
        _lastAppliedProgress = progress;
        bool enableDoor = progress >= MinProgressToEnableDoor;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;
            if (od.enabled != enableDoor) od.enabled = enableDoor;
        }
    }

    void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);

    // =========================================================
    // タイプ演出ユーティリティ
    // =========================================================
    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeOneShot(line));
    }

    IEnumerator CoTypeOneShot(string line)
    {
        yield return StartCoroutine(CoTypeOne(line));       // 1文字ずつ
        yield return new WaitForSeconds(LineInterval);      // 終了後にもインターバルを適用
        BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    IEnumerator CoTypeLines(string[] lines)
    {
        if (lines == null || lines.Length == 0)
        {
            BottomText.gameObject.SetActive(false);
            _typing = null;
            yield break;
        }

        for (int li = 0; li < lines.Length; li++)
        {
            yield return StartCoroutine(CoTypeOne(lines[li]));
            // 各行のあとにもインターバルを必ず入れる
            yield return new WaitForSeconds(LineInterval);
        }

        if (HideWhenDone) BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    IEnumerator CoTypeOne(string text)
    {
        if (!BottomText) yield break;

        BottomText.text = "";
        BottomText.gameObject.SetActive(true);

        if (CharsPerSecond <= 0f)
        {
            BottomText.text = text;
            yield break;
        }

        float interval = 1f / CharsPerSecond;
        float acc = 0f; int i = 0;

        while (i < text.Length)
        {
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval; i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }
}
