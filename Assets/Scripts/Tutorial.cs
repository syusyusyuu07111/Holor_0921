using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ===== テキスト／タイプ演出 =====
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;
    public bool HideWhenDone = true;

    [TextArea] public string[] Step1Lines = { "……ここはどこだろう。", "さっきまでの記憶が曖昧だ。", "とにかく、出口を探さないと。" };
    [TextArea] public string[] Step3Lines = { "……何か音がしたぞ！", "周りを探してみよう。" };

    Coroutine _typing;

    // ===== 進行度参照 =====
    [Header("進行度参照")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;

    // ===== OpenDoor 制御 =====
    [Header("制御対象（OpenDoorのみ）")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue;

    // ===== ドア：ロック時の入力フック（Step2トリガ） =====
    [Header("ドア：ロック時の入力フック")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public string DoorLockedMessage = "ドアはあかないようだ…";
    public float DoorLockedCooldown = 1.2f;
    private float _doorMsgCD = 0f;

    private InputSystem_Actions _input;

    // ===== 初見パネル（Step4/Step5）＆一時停止 =====
    [Header("初見チュートリアル画像")]
    public GameObject Step4Panel_StateAny;   // 初めて幽霊が見えた
    public GameObject Step5Panel_State2;     // 初めてstate=2を見た

    private bool _didStep4 = false;
    private bool _didStep5 = false;
    private bool _pauseGate = false;         // パネル表示中の抑止

    // ===== 抽選開始（Step3）制御 =====
    [Header("幽霊スポナー（EnemyAI）")]
    public List<EnemyAI> Spawners = new();   // AutoStart=false にしておく
    public float StartSpawnDelayAfterStep2 = 2f; // Step2テキストが消えた後の待機

    private bool _didStep2 = false;          // ドアロックを初回検知したか
    private bool _didStep3 = false;          // 抽選開始を実行したか

    // === Step3 ノイズSE（テキストより“先”に鳴らすための専用SE） ===
    [Header("Step3 ノイズSE（テキストより先に鳴らす）")]
    public bool PlayNoiseOnStep3 = true;
    public AudioClip Step3NoiseSE;
    public float Step3NoiseVolume = 1.0f;
    public Vector2 Step3NoisePitchRange = new Vector2(0.95f, 1.05f);
    public Transform NoiseAt;                    // nullなら Player の位置
    public float Step3TextDelayAfterSE = 0.15f;  // SE 直後に少し間を置いてからテキスト表示

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
        _input.UI.Enable();

        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.AddListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.AddListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.AddListener(OnProgressChanged);
        }
    }

    private void OnDisable()
    {
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.RemoveListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.RemoveListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        }
        _input.Player.Disable();
        _input.UI.Disable();

        if (Time.timeScale == 0f) Time.timeScale = 1f; // 念のため復旧
    }

    private void Start()
    {
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }
        if (Step4Panel_StateAny) Step4Panel_StateAny.SetActive(false);
        if (Step5Panel_State2) Step5Panel_State2.SetActive(false);

        // 念のためスポナー自動開始は止める（AutoStart=false 推奨だが、保険で止める）
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].StopSpawning();

        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);
        Step1();
    }

    private void Update()
    {
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        if (!_pauseGate) HandleLockedDoorTapFeedback(); // ポーズ中は抑止
    }

    // ====== Step2：ドアロック文言 → その後 Step3 を起動 ======
    void HandleLockedDoorTapFeedback()
    {
        if (!Player) return;
        if (_doorMsgCD > 0f) { _doorMsgCD -= Time.deltaTime; return; }

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();

        if (!pressed) return;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;
            if (od.enabled) continue; // すでに開けられる段階ならスルー
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // Step2：ロック文言
            ShowOneShot(DoorLockedMessage);
            _doorMsgCD = DoorLockedCooldown;

            // Step3 の予約（初回だけ）
            if (!_didStep2)
            {
                _didStep2 = true;
                StartCoroutine(CoAfterStep2_StartStep3());
            }
            break;
        }
    }

    IEnumerator CoAfterStep2_StartStep3()
    {
        // 表示が消えるのを UI 状態で待つより、タイプ終端で待つ方が安全
        while (_typing != null) yield return null;

        // さらに少し間を置く
        yield return new WaitForSeconds(StartSpawnDelayAfterStep2);

        // Step3 実行（抽選開始＋“まずSE”、その後テキスト）
        DoStep3();
    }

    public void DoStep3()
    {
        if (_didStep3) return;
        _didStep3 = true;
        StartCoroutine(CoDoStep3Sequence());
    }

    IEnumerator CoDoStep3Sequence()
    {
        // 1) 抽選開始（EnemyAI 側の SpawnLoop をスタート）
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].BeginSpawning();

        // 2) まずノイズSEを鳴らす（テキストより先）
        if (PlayNoiseOnStep3 && Step3NoiseSE)
        {
            var at = NoiseAt ? NoiseAt.position : (Player ? Player.position : Vector3.zero);
            float pitch = Random.Range(Step3NoisePitchRange.x, Step3NoisePitchRange.y);
            PlayClipAtPointPitch(Step3NoiseSE, at, Step3NoiseVolume, pitch);
        }

        // 3) 少し間を置いてからテキスト
        if (Step3TextDelayAfterSE > 0f)
            yield return new WaitForSeconds(Step3TextDelayAfterSE);

        if (Step3Lines != null && Step3Lines.Length > 0 && BottomText)
        {
            if (_typing != null) StopCoroutine(_typing);
            BottomText.gameObject.SetActive(true);
            _typing = StartCoroutine(CoTypeLines(Step3Lines));
        }
    }

    // ===== Step4：初めて幽霊が画面に映った =====
    public void Step4_ShowPanel()
    {
        if (_didStep4) return;
        _didStep4 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step4Panel_StateAny));
    }

    // ===== Step5：初めて state=2 の幽霊が映った =====
    public void Step5_ShowPanel()
    {
        if (_didStep5) return;
        _didStep5 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step5Panel_State2));
    }

    // ===== 共通：パネル表示→一時停止→UI.Submitで閉じる =====
    IEnumerator CoShowPausePanel(GameObject panel)
    {
        if (!panel) yield break;

        _pauseGate = true;

        panel.SetActive(true);
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        yield return null; // 1フレーム置く
        while (!_input.UI.Submit.WasPressedThisFrame())
            yield return null;

        panel.SetActive(false);
        Time.timeScale = prevScale;

        _pauseGate = false;
    }

    // ===== ドア制御 =====
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

    // ===== タイプ演出 =====
    public void Step1()
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines));
    }

    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeOneShot(line));
    }

    IEnumerator CoTypeOneShot(string line)
    {
        yield return StartCoroutine(CoTypeOne(line));
        yield return new WaitForSeconds(LineInterval);
        BottomText.gameObject.SetActive(false);
        _typing = null;
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

    IEnumerator CoTypeOne(string text)
    {
        BottomText.text = "";
        if (CharsPerSecond <= 0f) { BottomText.text = text; yield break; }

        float interval = 1f / CharsPerSecond;
        float acc = 0f; int i = 0;
        while (i < text.Length)
        {
            // ポーズ中のタイプを止めたいなら deltaTime、進めたいなら unscaledDeltaTime
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval; i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    // ===== 小ユーティリティ：ピッチ付き PlayClipAtPoint =====
    void PlayClipAtPointPitch(AudioClip clip, Vector3 pos, float vol, float pitch)
    {
        // ワンショット専用の一時AudioSourceを生成してすぐ破棄
        GameObject go = new GameObject("OneShotSE_Tutorial");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.volume = Mathf.Clamp01(vol);
        src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        src.PlayOneShot(clip, src.volume);
        Destroy(go, clip.length / Mathf.Max(0.1f, src.pitch));
    }
}
