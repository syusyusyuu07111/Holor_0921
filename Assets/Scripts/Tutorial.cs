using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ============================================================
    // Tutorial.cs
    // ============================================================
    //
    // 目的
    // ・序盤の導線（前段チュートリアル：移動→視点→ダッシュ）を提示
    // ・ドアがロック中の入力に反応して「開かない」メッセージを出す
    // ・進行度(HintText.ProgressStage)に応じてドアを解錠/維持する
    // ・幽霊スポーン開始（Step2後ディレイ、または外部強制）
    // ・幽霊初見/State2初見/隠れる初見などでパネル表示（TimeScale=0で停止）
    // ・ミッションテキスト（別UI）で次の目的を表示
    // ・Attack連打で前段チュートリアルをスキップ可能
    //
    // 重要：競合対策（テキスト表示）
    // ・_typingToken で「最新のテキスト要求」だけが表示を継続できる
    // ・後から来た ShowOneShot / Step1 / HintTutorial 等が優先される
    //
    // 2部屋目以降
    // ・StopTutorialForSecondRoom() を呼ぶと、このコンポーネントごと停止する
    //
    // ============================================================

    // ========== メインテキスト ==========
    [Header("メインテキスト")]
    public TextMeshProUGUI BottomText;        // 画面下の会話テキスト
    public TextMeshProUGUI TutoriaSkipText;   // 「連打でスキップ」などの表示
    public float CharsPerSecond = 40f;        // 文字送り速度
    public float LineInterval = 0.6f;         // 行間ウェイト
    public bool HideWhenDone = true;          // 打ち終わったら非表示にするか

    // Step1（導入）と Step3（幽霊が湧いたら）で出す台詞セット
    [TextArea] public string[] Step1Lines = { "……ここはどこだろう。", "さっきまでの記憶が曖昧だ。", "とにかく、出口を探さないと。" };
    [TextArea] public string[] Step3Lines = { "……何か音がしたぞ！", "周りを探してみよう。" };
    private Coroutine _typing;

    // 「どのリクエストが最新か」を表すトークン（コルーチン競合回避）
    private int _typingToken = 0;

    // ========== メッセージ ==========
    [Header("ロック/解錠メッセージ")]
    [TextArea] public string DoorLockedMessage = "ドアはあかないようだ…";
    [TextArea] public string DoorUnlockedMessage = "ドアが開いたようだ";
    private bool _didAnnounceDoorUnlocked = false; // 解錠通知は1回だけ

    // ========== 進行度/HINT ==========
    [Header("進行度参照")]
    public HintText HintRef;                 // 進行度と各種イベント元
    public bool AutoFindHintRef = true;      // 自動取得するか
    public int MinProgressToEnableDoor = 1;  // ここ以上でドア解錠扱い

    // ========== ドア制御 ==========
    [Header("制御対象（OpenDoor のみ）")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue; // 進行度が変わったら反映
    private bool _doorUnlockedOnce = false;          // 一度解錠したら永続解錠扱い

    // ========== ドア入力フック ==========
    [Header("ドア：ロック時の入力フック")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public float DoorLockedCooldown = 1.0f; // 「開かない」連打防止
    private float _doorMsgCD = 0f;

    private InputSystem_Actions _input;

    // ========== 初見パネル ==========
    [Header("初見チュートリアル画像")]
    public GameObject Step4Panel_StateAny; // 幽霊初見
    public GameObject Step5Panel_State2;   // State2初見
    private bool _didStep4 = false;
    private bool _didStep5 = false;

    [Header("隠れるチュートリアル画像")]
    public HideCroset HideRef;
    public GameObject HidePanel;
    private bool _didHidePanel = false;
    private bool _pendingHidePanel = false; // パネル表示要求だけ先に来た場合に保留
    private bool _pauseGate = false;        // パネル表示中の入力ゲート

    // ========== スポーン/Step3 ==========
    [Header("幽霊スポナー（EnemyAI）")]
    public List<EnemyAI> Spawners = new();
    public float StartSpawnDelayAfterStep2 = 2f;
    private bool _didStep2 = false;
    private bool _didStep3 = false;
    private bool _step3TextShown = false; // Step3テキストは1回

    // ========== 前段チュートリアル ==========
    [Header("前段チュートリアル（移動／視点／ダッシュ）")]
    public bool EnableBasicTutorial = true;
    public Transform CameraTransform;
    public PlayerController PlayerCtrl;

    [TextArea] public string BasicMoveText = "移動してみよう（WASD / 左スティック）";
    [TextArea] public string BasicLookText = "カメラを動かしてみよう（マウス / 右スティック）";
    [TextArea] public string BasicDashText = "シフトを押しながらダッシュしてみよう";
    [TextArea] public string BasicDoneText = "OK！準備完了。";

    [Header("前段チュートリアル：しきい値")]
    public float BasicLookYawTotal = 20f;
    public float BasicLookPitchTotal = 10f;
    public float BasicMoveMinDuration = 0.15f;
    public float BasicDashMinDuration = 0.15f;
    public float BasicMoveTotalDistanceRequired = 1.5f;
    public bool BasicMoveCountOnlyWhenInput = true;
    public float BasicMoveMaxStepPerFrame = 2.0f;

    private bool _basicRunning = false;
    private bool _basicDone = false;
    private Quaternion _basicPrevCamRot;
    private float _basicAccYaw = 0f;
    private float _basicAccPitch = 0f;
    private Vector3 _basicMovePrevPos;
    private float _basicMoveTotal = 0f;
    private Coroutine _basicCo;

    // ========== Attack×3 で前段スキップ ==========
    [Header("スキップ（Attack連打）")]
    public bool EnableAttackSkip = true;
    public int AttackSkipRequired = 3;
    public float AttackSkipWindow = 2.0f;
    public float SkipDoneHoldSeconds = 2.0f; // スキップ後「準備完了。」を出す時間（Realtime）

    private int _attackSkipCount = 0;
    private float _attackSkipTimer = 0f;

    // ========== ポーズ時のオーディオ ==========
    [Header("ポーズ時のオーディオ制御")]
    public bool PauseAudioWhilePanel = true;
    private bool _prevListenerPause = false;

    // ========== Hint 連携：キュー ==========
    private readonly Queue<string[]> _queuedHintTutorials = new Queue<string[]>();

    // ========== ミッションUI ==========
    [Header("ドア用ミッション（別テキストUI）")]
    public bool EnableDoorMission = true;
    public TextMeshProUGUI MissionText;
    public float MissionCharsPerSecond = 40f;
    public float MissionLineInterval = 0.4f;
    public bool MissionHideWhenDone = false;

    [TextArea] public string Mission_DoorCheck = "ドアをしらべてみよう";
    [TextArea] public string Mission_FindGhost = "次は近くにいる幽霊を見つけてみよう";
    [TextArea] public string Mission_HearVoiceGoNext = "次は幽霊の声を聞いて次の部屋に行こう";
    [TextArea] public string Mission_AllDone = "ミッション完了";

    private enum DoorMissionStage { None, DoorCheck, FindGhost, HearVoiceGoNext, AllDone }
    private DoorMissionStage _doorMission = DoorMissionStage.None;
    private Coroutine _typingMission;
    private bool _heardVoice = false;

    // ========== ライト ==========
    [Header("チュートリアル中は非表示にするライト")]
    public List<GameObject> LightsToToggle = new List<GameObject>();
    public bool HideLightsUntilMission3 = true;
    private bool _lightsActivatedAfterM3 = false;

    // ===== 2部屋目以降の停止フラグ =====
    private bool _stoppedForSecondRoom = false;

    // ===== 共通ゲート =====
    private bool IsEventAllowed() => !_stoppedForSecondRoom && (!EnableBasicTutorial || _basicDone);

    private void SkipCurrentTyping()
    {
        // 今までのテキストリクエストを全部無効化
        _typingToken++;

        if (_typing != null)
        {
            StopCoroutine(_typing);
            _typing = null;
        }
        if (BottomText) BottomText.gameObject.SetActive(false);
    }

    /// <summary>
    /// 外部（2部屋目など）から「とにかく幽霊スポーンだけ始めたい」ときに呼ぶ
    /// </summary>
    public void ForceStartSpawners()
    {
        if (Spawners == null) return;

        for (int i = 0; i < Spawners.Count; i++)
        {
            if (Spawners[i] != null)
            {
                Spawners[i].BeginSpawning();
            }
        }
    }

    public void ReapplyDoorByCurrentProgress()
    {
        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);
    }

    //=========== ドア関連公開API ===========
    public void ForceUnlockDoors()
    {
        _doorUnlockedOnce = true;
        int progressForDoor = HintRef ? HintRef.ProgressStage : 0;
        if (progressForDoor < MinProgressToEnableDoor) progressForDoor = MinProgressToEnableDoor;
        ApplyDoorEnableByProgress(progressForDoor);
    }

    /// <summary>
    /// 2つ目の部屋に入ったなどのタイミングで、
    /// このチュートリアルの動きを止める（UIコンポーネント自体は残す）
    /// </summary>
    public void StopTutorialForSecondRoom()
    {
        // 2部屋目以降はこのチュートリアルを完全停止扱いにする
        _stoppedForSecondRoom = true;

        // 念のため、このコンポーネント内の全コルーチンを止める
        StopAllCoroutines();

        // 参照をクリア（個別 StopCoroutine は StopAllCoroutines 済みなので null だけ）
        _typing = null;
        _typingMission = null;
        _basicCo = null;

        // 以降、前段チュートリアルやイベントが動かないようにする
        EnableBasicTutorial = false;
        _basicRunning = false;
        _basicDone = true;

        _pauseGate = true;                // 新しいパネルなども出さない
        _queuedHintTutorials.Clear();     // キューされているヒント演出も破棄

        // テキストの中身だけクリア（UIオブジェクトは残す）
        if (BottomText != null)
        {
            BottomText.text = "";
            BottomText.gameObject.SetActive(false);
        }
        if (MissionText != null)
        {
            MissionText.text = "";
            MissionText.gameObject.SetActive(false);
        }
        if (TutoriaSkipText != null)
        {
            TutoriaSkipText.text = "";
            TutoriaSkipText.gameObject.SetActive(false);
        }

        // パネル類も閉じる
        if (Step4Panel_StateAny) Step4Panel_StateAny.SetActive(false);
        if (Step5Panel_State2) Step5Panel_State2.SetActive(false);
        if (HidePanel) HidePanel.SetActive(false);

        // このコンポーネント自体を止める（Update / イベント処理も停止）
        enabled = false;

        Debug.Log("Tutorial: StopTutorialForSecondRoom 実行、ロジック停止＆テキストクリア");
    }

    // ========== ライフサイクル ==========
    private void Awake()
    {
        // HintRef を自動で探す（必要なら非アクティブも対象）
        if (AutoFindHintRef && !HintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = UnityEngine.Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            HintRef = UnityEngine.Object.FindFirstObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = UnityEngine.Object.FindObjectOfType<HintText>();
#endif
        }
        _input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        _input.UI.Enable();

        // Hint側イベントを受け取る（初見パネル/進行/ヒント表示など）
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.AddListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.AddListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.AddListener(OnProgressChanged);
            HintRef.OnLineFullyRevealed.AddListener(OnHintAllRevealed);
            HintRef.OnHintTutorialLinesRequested.AddListener(OnHintTutorialLinesRequested);
        }

        // 隠れ初見パネル
        if (HideRef) HideRef.OnFirstHidePromptShown.AddListener(ShowHidePanelOnce);

        // 前段チュートリアル開始
        if (EnableBasicTutorial) _basicCo = StartCoroutine(CoRunBasicTutorial());

        // ミッション開始
        if (EnableDoorMission) StartDoorMissionIfNeeded();

        // 幽霊スポナーのイベント登録（テキスト演出用）
        if (Spawners != null)
        {
            for (int i = 0; i < Spawners.Count; i++)
            {
                if (Spawners[i])
                    Spawners[i].OnGhostSpawned.AddListener(OnAnyGhostSpawned_FirstTime);
            }
        }
    }

    private void OnDisable()
    {
        // Hint側イベント解除
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.RemoveListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.RemoveListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
            HintRef.OnLineFullyRevealed.RemoveListener(OnHintAllRevealed);
            HintRef.OnHintTutorialLinesRequested.RemoveListener(OnHintTutorialLinesRequested);
        }
        if (HideRef) HideRef.OnFirstHidePromptShown.RemoveListener(ShowHidePanelOnce);

        // 入力無効化
        if (_input != null)
        {
            _input.Player.Disable();
            _input.UI.Disable();
        }

        // スポナー解除
        if (Spawners != null)
        {
            for (int i = 0; i < Spawners.Count; i++)
            {
                if (Spawners[i])
                    Spawners[i].OnGhostSpawned.RemoveListener(OnAnyGhostSpawned_FirstTime);
            }
        }
    }

    private void OnDestroy()
    {
        try
        {
            if (_basicCo != null) { StopCoroutine(_basicCo); _basicCo = null; }

            if (_input != null)
            {
                _input.Player.Disable();
                _input.UI.Disable();
                _input.Dispose();
                _input = null;
            }
        }
        catch { }
    }

    private void Start()
    {
        // UI初期化
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }
        if (TutoriaSkipText) TutoriaSkipText.enabled = (EnableBasicTutorial && !_basicDone);
        if (Step4Panel_StateAny) Step4Panel_StateAny.SetActive(false);
        if (Step5Panel_State2) Step5Panel_State2.SetActive(false);
        if (HidePanel) HidePanel.SetActive(false);

        // 最初はスポーン停止
        if (Spawners != null)
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].StopSpawning();

        // ドアの状態を進行度で反映
        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);

        // 前段が無い or 完了済みならすぐ Step1
        if (!EnableBasicTutorial || _basicDone)
        {
            Step1();
        }

        // Hint由来のチュートリアルテキストが溜まっていれば出す
        if (_queuedHintTutorials.Count > 0 && !_pauseGate && IsEventAllowed() && _typing == null)
        {
            var pending = _queuedHintTutorials.Dequeue();
            ShowHintTutorialLinesNow(pending);
        }

        // ミッションUI初期化
        if (MissionText) { MissionText.text = ""; MissionText.gameObject.SetActive(false); }

        // ライトはミッション3まで隠す設定なら消す
        if (HideLightsUntilMission3 && LightsToToggle != null)
            for (int i = 0; i < LightsToToggle.Count; i++)
                if (LightsToToggle[i]) LightsToToggle[i].SetActive(false);
    }

    private void Update()
    {
        // Player参照が無いときはタグで拾う
        if (!Player)
        {
#if UNITY_2023_1_OR_NEWER
            var p = GameObject.FindWithTag("Player");
#else
            var p = GameObject.FindGameObjectWithTag("Player");
#endif
            Player = p ? p.transform : null;
        }

        // Attack連打スキップ処理
        HandleAttackSkip();

        // 進行度が変わったらドア反映
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        // ロック中ドアに対する入力メッセージ
        if (!_pauseGate) HandleLockedDoorTapFeedback();

        // ミッション3（声を聞いて次へ）中、解錠ドアに触れたら完了扱い
        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext && !_pauseGate && IsEventAllowed())
            TryCompleteDoorMissionByEnabledDoorInteract();
    }

    // ========== Attack×3 スキップ ==========
    private void HandleAttackSkip()
    {
        if (!EnableAttackSkip) return;
        if (_input == null || !_input.Player.enabled) return;

        // 前段が走ってないならスキップ処理は無効化
        if (!(EnableBasicTutorial && !_basicDone))
        {
            _attackSkipCount = 0;
            _attackSkipTimer = 0f;
            return;
        }

        // ウィンドウタイマー
        if (_attackSkipTimer > 0f)
        {
            _attackSkipTimer -= Time.deltaTime;
            if (_attackSkipTimer <= 0f) { _attackSkipTimer = 0f; _attackSkipCount = 0; }
        }

        // Attack押下カウント
        if (_input.Player.Attack.WasPressedThisFrame())
        {
            if (_attackSkipTimer <= 0f)
            {
                _attackSkipTimer = Mathf.Max(0.01f, AttackSkipWindow);
                _attackSkipCount = 1;
                if (TutoriaSkipText) TutoriaSkipText.enabled = false;
            }
            else
            {
                _attackSkipCount++;
            }

            // 規定回数でスキップ
            if (_attackSkipCount >= Mathf.Max(1, AttackSkipRequired))
            {
                ForceSkipBasicTutorialNow();
                _attackSkipCount = 0;
                _attackSkipTimer = 0f;
            }
        }
    }

    private void ForceSkipBasicTutorialNow()
    {
        // 走ってるコルーチンを一旦全部止める
        StopAllCoroutines();

        _basicCo = null;
        _typing = null;

        _basicRunning = false;
        _basicDone = true;

        // 「準備完了。」を出す
        if (BottomText)
        {
            BottomText.gameObject.SetActive(true);
            BottomText.text = BasicDoneText;
        }

        if (TutoriaSkipText) TutoriaSkipText.enabled = false;

        // 一呼吸おいてから本編Step1開始
        StartCoroutine(CoAfterSkipStartStep1());
    }

    private IEnumerator CoAfterSkipStartStep1()
    {
        yield return new WaitForSecondsRealtime(SkipDoneHoldSeconds);

        Step1();
        if (EnableDoorMission) StartDoorMissionIfNeeded();
    }

    // ========== Step1/2/3 ==========
    public void Step1()
    {
        if (_stoppedForSecondRoom) return;
        if (!BottomText) return;

        // 最新リクエストとしてトークン更新
        _typingToken++;

        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines, _typingToken));
    }

    private void HandleLockedDoorTapFeedback()
    {
        if (!IsEventAllowed()) return;
        if (!Player) return;

        // クールダウン中は何もしない
        if (_doorMsgCD > 0f) { _doorMsgCD -= Time.deltaTime; return; }

        // ロック中ドアの「触ろうとした入力」
        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();

        if (!pressed) return;

        // 近いロックドアがあればメッセージを出す
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            bool locked = true;
            try { locked = od.IsLocked; } catch { locked = !od.enabled; }

            if (!locked) continue;

            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            // 必要なら「ドアの正面側にいるか」チェック
            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // 今の表示を中断して「開かない」表示
            SkipCurrentTyping();
            ShowOneShot(DoorLockedMessage);
            _doorMsgCD = DoorLockedCooldown;

            // ミッション：ドア確認→幽霊探しへ
            if (EnableDoorMission && _doorMission == DoorMissionStage.DoorCheck)
                AdvanceDoorMissionTo(DoorMissionStage.FindGhost);

            // Step2扱い（初回だけ）→ 少し待ってスポーン開始へ
            if (!_didStep2)
            {
                _didStep2 = true;
                StartCoroutine(CoAfterStep2_StartStep3());
            }
            break;
        }
    }

    private IEnumerator CoAfterStep2_StartStep3()
    {
        // BottomTextが消えるのを待つ（演出の順番保証）
        while (BottomText && BottomText.gameObject.activeSelf) yield return null;

        yield return new WaitForSeconds(StartSpawnDelayAfterStep2);
        DoStep3();
    }

    public void DoStep3()
    {
        if (_didStep3) return;
        _didStep3 = true;
        if (!IsEventAllowed()) return;

        // ここでスポーン開始
        if (Spawners != null)
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].BeginSpawning();
    }

    // 幽霊が初めて湧いた時のコールバック
    private void OnAnyGhostSpawned_FirstTime()
    {
        if (!IsEventAllowed()) return;

        if (_step3TextShown) return;
        _step3TextShown = true;

        if (Step3Lines != null && Step3Lines.Length > 0)
        {
            // 後から来たこのテキストを優先
            _typingToken++;

            if (_typing != null) StopCoroutine(_typing);
            if (BottomText)
            {
                BottomText.gameObject.SetActive(true);
                _typing = StartCoroutine(CoTypeLines(Step3Lines, _typingToken));
            }
        }
    }

    // ========== 初見パネル ==========
    public void Step4_ShowPanel()
    {
        if (!IsEventAllowed() || _didStep4 || _pauseGate) return;
        _didStep4 = true;

        // 幽霊初見パネル
        StartCoroutine(CoShowPausePanel(Step4Panel_StateAny));

        // ミッション：幽霊探し→声を聞いて次へ
        if (EnableDoorMission && _doorMission == DoorMissionStage.FindGhost)
            AdvanceDoorMissionTo(DoorMissionStage.HearVoiceGoNext);
    }

    public void Step5_ShowPanel()
    {
        if (!IsEventAllowed() || _didStep5 || _pauseGate) return;
        _didStep5 = true;

        // State2初見パネル
        StartCoroutine(CoShowPausePanel(Step5Panel_State2));

        _heardVoice = true;
        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext)
            ShowMissionText(Mission_HearVoiceGoNext);
    }

    public void ShowHidePanelOnce()
    {
        if (_didHidePanel) return;

        // パネル表示中などは保留して後で出す
        if (_pauseGate || !IsEventAllowed()) { _pendingHidePanel = true; return; }

        _didHidePanel = true;
        StartCoroutine(CoShowPausePanel(HidePanel));
    }

    private IEnumerator CoShowPausePanel(GameObject panel)
    {
        if (!panel) yield break;

        _pauseGate = true;

        // 表示＆（任意で）音停止
        panel.SetActive(true);
        _prevListenerPause = AudioListener.pause;
        if (PauseAudioWhilePanel) AudioListener.pause = true;

        // ゲーム停止（TimeScale=0）
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        // クリック待ち（UI.Click）
        yield return null;
        while (!_input.UI.Click.WasPressedThisFrame()) yield return null;

        // 閉じる＆復帰
        panel.SetActive(false);
        if (PauseAudioWhilePanel) AudioListener.pause = _prevListenerPause;

        Time.timeScale = prevScale;
        _pauseGate = false;

        // 保留していた HidePanel を出す（条件OKなら）
        if (_pendingHidePanel && IsEventAllowed())
        {
            _pendingHidePanel = false;
            if (!_didHidePanel && HidePanel)
            {
                _didHidePanel = true;
                StartCoroutine(CoShowPausePanel(HidePanel));
            }
        }
    }

    // ========== ドア制御 ==========
    private void ApplyDoorEnableByProgress(int progress)
    {
        _lastAppliedProgress = progress;

        // 進行度で解錠扱いか
        bool unlockedByProgress = progress >= MinProgressToEnableDoor;
        if (unlockedByProgress) _doorUnlockedOnce = true;

        // 一度でも解錠に到達したら、以後は永続解錠
        bool doorShouldBeUnlocked = _doorUnlockedOnce;

        bool anyJustUnlocked = false;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            if (!od.enabled) od.enabled = true;

            bool hadProperty = true;
            try
            {
                // OpenDoorが IsLocked / SetLocked を持つ想定
                bool lockedNow = od.IsLocked;
                bool wantLocked = !doorShouldBeUnlocked;

                if (lockedNow != wantLocked) od.SetLocked(wantLocked);
                if (lockedNow && !wantLocked) anyJustUnlocked = true;
            }
            catch
            {
                // IsLocked/SetLocked が無い場合は enabled で代用
                hadProperty = false;
            }

            if (!hadProperty)
            {
                bool wasEnabled = od.enabled;
                od.enabled = doorShouldBeUnlocked;
                if (!wasEnabled && od.enabled) anyJustUnlocked = true;
            }
        }

        // 解錠通知（1回だけ）
        if ((unlockedByProgress || doorShouldBeUnlocked) && anyJustUnlocked && !_didAnnounceDoorUnlocked && !_pauseGate)
        {
            ShowOneShot(string.IsNullOrEmpty(DoorUnlockedMessage) ? "ドアが開いたようだ" : DoorUnlockedMessage);
            _didAnnounceDoorUnlocked = true;
        }
    }

    private void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);

    // ========== Hint連携 ==========
    private void OnHintAllRevealed(string id)
    {
        if (id == "state1.element0")
        {
            // 必要ならここに反応処理を追加できる（現状は空）
        }
    }

    private void OnHintTutorialLinesRequested(string[] lines)
    {
        // 空なら無視
        if (!HasAnyContent(lines)) return;

        // 今表示できないならキューへ
        if (!IsEventAllowed() || _pauseGate || _typing != null)
        {
            _queuedHintTutorials.Enqueue(DuplicateLines(lines));
            return;
        }

        // すぐ表示
        ShowHintTutorialLinesNow(lines);
    }

    private void ShowHintTutorialLinesNow(string[] lines)
    {
        if (_stoppedForSecondRoom) return;
        if (!BottomText) return;

        // Hint からのテキストも最新としてトークン更新
        _typingToken++;

        var copy = DuplicateLines(lines);
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(copy, _typingToken));
    }

    private string[] DuplicateLines(string[] source)
    {
        if (source == null || source.Length == 0) return Array.Empty<string>();
        var copy = new string[source.Length];
        for (int i = 0; i < source.Length; i++) copy[i] = source[i];
        return copy;
    }

    private bool HasAnyContent(string[] lines)
    {
        if (lines == null) return false;
        for (int i = 0; i < lines.Length; i++)
            if (!string.IsNullOrWhiteSpace(lines[i])) return true;
        return false;
    }

    // ========== ミッションUI ==========
    private void StartDoorMissionIfNeeded()
    {
        // すでに始まっていたら何もしない
        if (_doorMission != DoorMissionStage.None) return;

        // 前段中はミッション開始しない
        if (EnableBasicTutorial && !_basicDone) return;

        _doorMission = DoorMissionStage.DoorCheck;
        ShowMissionText(Mission_DoorCheck);
    }

    private void AdvanceDoorMissionTo(DoorMissionStage next)
    {
        _doorMission = next;

        switch (_doorMission)
        {
            case DoorMissionStage.FindGhost:
                ShowMissionText(Mission_FindGhost);
                break;

            case DoorMissionStage.HearVoiceGoNext:
                ShowMissionText(Mission_HearVoiceGoNext);
                if (HideLightsUntilMission3) ActivateLightsAfterMission3();
                break;

            case DoorMissionStage.AllDone:
                ShowMissionText(Mission_AllDone);
                if (MissionHideWhenDone && MissionText) StartCoroutine(CoHideMissionAfter(MissionLineInterval));
                break;
        }
    }

    private void ShowMissionText(string line)
    {
        if (_stoppedForSecondRoom) return;
        if (!MissionText || string.IsNullOrEmpty(line)) return;

        if (_typingMission != null) { StopCoroutine(_typingMission); _typingMission = null; }

        MissionText.gameObject.SetActive(true);
        _typingMission = StartCoroutine(CoTypeOne_Mission(line));
    }

    private IEnumerator CoTypeOne_Mission(string text)
    {
        MissionText.text = "";

        // 0以下なら即表示
        if (MissionCharsPerSecond <= 0f)
        {
            MissionText.text = text;
            yield break;
        }

        float interval = 1f / MissionCharsPerSecond;
        float acc = 0f;
        int i = 0;

        while (i < text.Length)
        {
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval;
                i++;
                MissionText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    private IEnumerator CoHideMissionAfter(float wait)
    {
        yield return new WaitForSeconds(wait);
        if (MissionText) MissionText.gameObject.SetActive(false);
    }

    private void TryCompleteDoorMissionByEnabledDoorInteract()
    {
        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame();

        if (!pressed || !Player) return;

        // 解錠ドアに触れたらミッション完了
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            bool unlocked = true;
            try { unlocked = !od.IsLocked; } catch { unlocked = od.enabled; }

            if (!unlocked) continue;

            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            AdvanceDoorMissionTo(DoorMissionStage.AllDone);
            break;
        }
    }

    private void ActivateLightsAfterMission3()
    {
        if (_lightsActivatedAfterM3) return;
        _lightsActivatedAfterM3 = true;

        if (LightsToToggle == null) return;
        for (int i = 0; i < LightsToToggle.Count; i++)
            if (LightsToToggle[i]) LightsToToggle[i].SetActive(true);
    }

    // ========== テキスト演出（BottomText用・トークン対応） ==========
    public void ShowOneShot(string line)
    {
        if (_stoppedForSecondRoom) return;
        if (!BottomText || string.IsNullOrEmpty(line)) return;

        // これ以降のテキストを最新として扱う
        _typingToken++;

        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeOneShot(line, _typingToken));
    }

    private IEnumerator CoTypeOneShot(string line, int token)
    {
        // 本文
        yield return StartCoroutine(CoTypeOne(line, token));
        if (token != _typingToken) yield break;

        // 行間ウェイト
        yield return new WaitForSeconds(LineInterval);
        if (token != _typingToken) yield break;

        BottomText.gameObject.SetActive(false);

        if (token == _typingToken)
            _typing = null;
    }

    private IEnumerator CoTypeLines(string[] lines, int token)
    {
        for (int li = 0; li < lines.Length; li++)
        {
            yield return StartCoroutine(CoTypeOne(lines[li], token));
            if (token != _typingToken) yield break;

            if (li < lines.Length - 1)
            {
                yield return new WaitForSeconds(LineInterval);
                if (token != _typingToken) yield break;
            }
        }

        if (token != _typingToken) yield break;

        if (HideWhenDone) BottomText.gameObject.SetActive(false);

        if (token == _typingToken)
            _typing = null;
    }

    private IEnumerator CoTypeOne(string text, int token)
    {
        BottomText.text = "";

        if (CharsPerSecond <= 0f)
        {
            if (token == _typingToken)
                BottomText.text = text;
            yield break;
        }

        float interval = 1f / CharsPerSecond;
        float acc = 0f;
        int i = 0;

        while (i < text.Length && token == _typingToken)
        {
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length && token == _typingToken)
            {
                acc -= interval;
                i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    // ========== 前段チュートリアル本体 ==========
    private IEnumerator CoRunBasicTutorial()
    {
        if (_basicRunning || _basicDone) yield break;
        if (!BottomText) yield break;

        _basicRunning = true;

        // 視点計測のために初期回転を保存
        if (CameraTransform) _basicPrevCamRot = CameraTransform.rotation;
        yield return null;

        // この前段チュートリアル中のテキスト用トークン
        int token = ++_typingToken;

        // -------------------------
        // 1) 移動
        // -------------------------
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicMoveText, token));

        _basicMoveTotal = 0f;
        _basicMovePrevPos = Player ? Player.position : Vector3.zero;

        float moveTimer = 0f;
        while (true)
        {
            if (_basicDone) { _basicRunning = false; yield break; }

            // PlayerController があればそっちを優先、無ければ入力で判定
            bool moving = PlayerCtrl
                ? PlayerCtrl.IsMovingNow
                : (_input.Player.Move.ReadValue<Vector2>() != Vector2.zero);

            // 移動距離積算（Yは無視）
            if (Player)
            {
                Vector3 cur = Player.position;
                Vector3 delta = cur - _basicMovePrevPos;
                delta.y = 0f;

                float step = Mathf.Min(delta.magnitude, BasicMoveMaxStepPerFrame);
                if (!BasicMoveCountOnlyWhenInput || moving) _basicMoveTotal += step;

                _basicMovePrevPos = cur;
            }

            // 一定時間動いている＋累計距離を満たしたらクリア
            if (moving) moveTimer += Time.deltaTime;
            else moveTimer = 0f;

            if (moveTimer >= BasicMoveMinDuration && _basicMoveTotal >= BasicMoveTotalDistanceRequired) break;
            yield return null;
        }

        // -------------------------
        // 2) 視点
        // -------------------------
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicLookText, token));

        _basicAccYaw = 0f;
        _basicAccPitch = 0f;

        while (true)
        {
            if (_basicDone) { _basicRunning = false; yield break; }

            if (CameraTransform)
            {
                Quaternion cur = CameraTransform.rotation;

                // forwardベクトルから yaw/pitch の変化を積算
                Vector3 fPrev = _basicPrevCamRot * Vector3.forward;
                Vector3 fCur = cur * Vector3.forward;

                float yawPrev = Mathf.Atan2(fPrev.x, fPrev.z) * Mathf.Rad2Deg;
                float yawCur = Mathf.Atan2(fCur.x, fCur.z) * Mathf.Rad2Deg;
                float dyaw = Mathf.DeltaAngle(yawPrev, yawCur);
                _basicAccYaw += Mathf.Abs(dyaw);

                float pitchPrev = Mathf.Asin(Mathf.Clamp(fPrev.y, -1f, 1f)) * Mathf.Rad2Deg;
                float pitchCur = Mathf.Asin(Mathf.Clamp(fCur.y, -1f, 1f)) * Mathf.Rad2Deg;
                float dpitch = Mathf.DeltaAngle(pitchPrev, pitchCur);
                _basicAccPitch += Mathf.Abs(dpitch);

                _basicPrevCamRot = cur;

                if (_basicAccYaw >= BasicLookYawTotal && _basicAccPitch >= BasicLookPitchTotal) break;
            }
            yield return null;
        }

        // -------------------------
        // 3) ダッシュ
        // -------------------------
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicDashText, token));

        float dashTimer = 0f;
        float decayPerSec = 0.5f;

        while (true)
        {
            if (_basicDone) { _basicRunning = false; yield break; }

            bool dashing = PlayerCtrl ? PlayerCtrl.IsDashingNow : false;

            // ダッシュしている間だけ加算。してない時は少し減衰させる（誤差に強い）
            if (dashing) dashTimer += Time.deltaTime;
            else dashTimer = Mathf.Max(0f, dashTimer - Time.deltaTime * decayPerSec);

            if (dashTimer >= BasicDashMinDuration) break;
            yield return null;
        }

        // -------------------------
        // 4) 完了
        // -------------------------
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        BottomText.text = BasicDoneText;

        _basicDone = true;
        _basicRunning = false;

        if (TutoriaSkipText) TutoriaSkipText.enabled = false;

        // 保留していた HidePanel があれば出す
        if (_pendingHidePanel)
        {
            _pendingHidePanel = false;
            if (!_pauseGate && !_didHidePanel && HidePanel)
            {
                _didHidePanel = true;
                StartCoroutine(CoShowPausePanel(HidePanel));
            }
        }

        // 本編の導入へ
        Step1();
        if (EnableDoorMission) StartDoorMissionIfNeeded();
    }
}