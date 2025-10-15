using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ===== 画面下テキスト（タイプ演出） =====
    [Header("共通テキスト")]
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;
    public bool HideWhenDone = true;

    [TextArea]
    public string[] Step1Lines = { "……ここはどこだろう。", "さっきまでの記憶が曖昧だ。", "とにかく、出口を探さないと。" };

    // Step2：ドアが開かないメッセージ
    [Header("Step2（ロック中ドア）")]
    public string DoorLockedMessage = "ドアはあかないようだ…";
    public Transform Player;                    // ドア近接判定用
    public List<OpenDoor> DoorScripts = new();  // 有効/無効を切り替える対象（OpenDoorコンポーネントのみ）
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public float DoorLockedCooldown = 1.2f;
    private float _doorMsgCD = 0f;

    // Step3：初めて湧いた瞬間の一言（SEは無しでテキストのみ）
    [Header("Step3（初湧きリアクション）")]
    [TextArea]
    public string Step3Line = "……今の音は？　近くを探してみよう。";
    public List<EnemyAI> EnemyControllers = new(); // ここにEnemyAIを登録
    public float EnemySpawnEnableDelay = 2.0f;     // Step2のテキストが消えてから何秒後に抽選開始
    private bool _spawnWasEnabled = false;         // 一度だけ抽選開始させる
    private bool _didFirstSpawnText = false;       // Step3を一度だけ

    // Step4：初めて画面に幽霊が映ったらパネル表示＆時間停止
    [Header("Step4（初見チュートリアル画像）")]
    public HintText HintRef;                       // Ghostがカメラに映った検出を持っている
    public bool AutoFindHintRef = true;
    public GameObject Step4Panel;                  // 表示するUI
    public bool PauseTimeOnStep4 = true;           // 時間停止ON
    private bool _didStep4 = false;
    private Coroutine _step4Co;

    // 進行度：0のときはOpenDoorを無効
    [Header("進行度")]
    public int MinProgressToEnableDoor = 1;
    private int _lastAppliedProgress = int.MinValue;

    // 入力
    private InputSystem_Actions _input;

    void Awake()
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

    void OnEnable()
    {
        _input.Player.Enable();
        _input.UI.Enable();

        // Ghostが“湧いた瞬間”→ Step3テキスト
        foreach (var ai in EnemyControllers)
            if (ai) ai.OnGhostSpawned.AddListener(OnFirstGhostSpawned);

        // Ghostが“画面に映った瞬間”→ Step4パネル
        if (HintRef) HintRef.OnFirstGhostSeen.AddListener(Step4);

        // 進行度変更でドアON/OFF（HintTextにイベントがある想定）
        if (HintRef) HintRef.OnProgressChanged.AddListener(OnProgressChanged);
    }

    void OnDisable()
    {
        foreach (var ai in EnemyControllers)
            if (ai) ai.OnGhostSpawned.RemoveListener(OnFirstGhostSpawned);

        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.RemoveListener(Step4);
            HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        }

        _input.UI.Disable();
        _input.Player.Disable();
    }

    void Start()
    {
        // 下テキスト初期化
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }

        // Step4 UI 初期非表示
        if (Step4Panel) Step4Panel.SetActive(false);

        // 進行度でドア有効/無効
        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);

        // Step1開始
        Step1();
    }

    void Update()
    {
        // 進行度ポーリング（保険）
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        // 進行度0のとき：ロック中ドアへインタラクトしたらStep2テキスト
        HandleLockedDoorTapFeedback();
    }

    // ====== Step1：導入テキスト ======
    public void Step1()
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines));
    }

    // ====== Step2：ドアが開かないメッセージ（表示→消えた後 → 2秒後に抽選開始） ======
    void HandleLockedDoorTapFeedback()
    {
        if (!Player) return;
        if (_doorMsgCD > 0f) { _doorMsgCD -= Time.deltaTime; return; }

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();

        if (!pressed) return;

        // 無効なOpenDoorに対してのみ反応
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;
            if (od.enabled) continue; // 開けられる状態ならスルー

            // 距離判定
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            // 正面限定ならチェック
            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // Step2テキスト表示
            ShowOneShot(DoorLockedMessage);

            // 表示終了を待ってから抽選開始（未開始のときだけ）
            if (!_spawnWasEnabled) StartCoroutine(CoEnableEnemySpawningAfterStep2());
            _doorMsgCD = DoorLockedCooldown;
            break;
        }
    }

    IEnumerator CoEnableEnemySpawningAfterStep2()
    {
        // 今出しているタイプ演出の終了を待つ
        while (_typing != null) yield return null;

        // 指定秒待機（デザイン要件：Step2テキストが消えてから2秒）
        yield return new WaitForSeconds(EnemySpawnEnableDelay);

        // 抽選開始（チュートリアルの合図でのみ）
        foreach (var ai in EnemyControllers)
            if (ai && !ai.IsSpawning) ai.BeginSpawning();

        _spawnWasEnabled = true;
    }

    // ====== Step3：初めて幽霊が湧いた瞬間の一言（SE無し） ======
    private void OnFirstGhostSpawned()
    {
        if (_didFirstSpawnText) return;   // 一度だけ
        _didFirstSpawnText = true;

        if (!string.IsNullOrEmpty(Step3Line))
            ShowOneShot(Step3Line);
    }

    // ====== Step4：初めて画面に幽霊が映ったら画像＋時間停止（UI.Submitで閉じる） ======
    public void Step4()
    {
        if (_didStep4) return;
        _didStep4 = true;

        if (_step4Co != null) StopCoroutine(_step4Co);
        _step4Co = StartCoroutine(CoStep4Panel());
    }

    IEnumerator CoStep4Panel()
    {
        if (Step4Panel) Step4Panel.SetActive(true);

        float prevTimeScale = Time.timeScale;
        if (PauseTimeOnStep4) Time.timeScale = 0f;

        // UI.Submitが押されるまで待機（時間停止中でもUpdateは回るのでOK）
        while (!_input.UI.Submit.WasPressedThisFrame())
            yield return null;

        if (Step4Panel) Step4Panel.SetActive(false);
        if (PauseTimeOnStep4) Time.timeScale = prevTimeScale;

        _step4Co = null;
    }

    // ====== 進行度 → ドアの有効/無効 ======
    void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);

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

    // ====== テキストユーティリティ ======
    Coroutine _typing;

    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeOneShot(line));
    }

    IEnumerator CoTypeLines(string[] lines)
    {
        for (int li = 0; li < lines.Length; li++)
        {
            yield return StartCoroutine(CoTypeOne(lines[li]));
            if (li < lines.Length - 1) yield return new WaitForSeconds(LineInterval);
        }
        if (HideWhenDone) BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    IEnumerator CoTypeOneShot(string line)
    {
        yield return StartCoroutine(CoTypeOne(line));
        yield return new WaitForSeconds(LineInterval);
        BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    IEnumerator CoTypeOne(string text)
    {
        BottomText.text = "";
        if (CharsPerSecond <= 0f) { BottomText.text = text; yield break; }

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
