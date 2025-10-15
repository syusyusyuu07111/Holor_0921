using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ===== 表示/タイプ設定 =====
    public TextMeshProUGUI BottomText;             // 画面下テキスト
    public float CharsPerSecond = 40f;             // 1秒あたり表示文字数
    public float LineInterval = 0.6f;              // 行と行の間の待機秒
    public bool HideWhenDone = true;               // すべて表示後に非表示
    public bool ApplyIntervalAfterLastLine = true; // 最後の行の後にもLineIntervalを適用

    [TextArea]
    public string[] Step1Lines =
    {
        "……ここはどこだろう。",
        "さっきまでの記憶が曖昧だ。",
        "とにかく、出口を探さないと。"
    };

    [TextArea]
    public string[] Step2Lines =
    {
        "ドアはあかないようだ…"
    };

    [TextArea]
    public string[] Step3Lines =
    {
        "何か音がしたぞ！",
        "周りを探してみよう"
    };

    // ===== 進行度/ドア =====
    [Header("進行度参照")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;

    [Header("制御対象（OpenDoorのみ）")]
    public List<OpenDoor> DoorScripts = new();

    // ===== ロック時の入力フック =====
    [Header("ドア：ロック時の入力フック")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public float DoorLockedCooldown = 1.2f;

    // ===== チュートリアル：スポーン制御 =====
    [Header("チュートリアル：スポーン制御")]
    public List<MonoBehaviour> SpawnScripts = new(); // EnemyAI 等（enabled ON/OFF）
    public bool AutoDisableSpawnersOnStart = true;
    public float EnableSpawnerDelayAfterStep2 = 2f;

    // --------------- ここから実装を追加（スポーンイベント購読） ---------------
    public List<EnemyAI> Spawners = new();          // EnemyAI を列挙
    public bool AutoFindSpawners = true;
    // --------------- ここまで実装を追加 ---------------

    // ----- 内部 -----
    private Coroutine _typing;
    private InputSystem_Actions _input;
    private int _lastAppliedProgress = int.MinValue;
    private float _doorMsgCD = 0f;

    private bool _isStep2Playing = false;
    private bool _spawnerUnlocked = false;
    private bool _step3Fired = false;

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

        // --------------- ここから実装を追加（スポーナー自動収集） ---------------
        if (AutoFindSpawners && (Spawners == null || Spawners.Count == 0))
        {
#if UNITY_2023_1_OR_NEWER
            var found = Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var found = FindObjectsOfType<EnemyAI>(true);
#endif
            if (found != null)
            {
                if (Spawners == null) Spawners = new List<EnemyAI>(found.Length);
                foreach (var sp in found) if (sp) Spawners.Add(sp);
            }
        }
        // --------------- ここまで実装を追加 ---------------
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        if (HintRef) HintRef.OnProgressChanged.AddListener(OnProgressChanged);

        // --------------- ここから実装を追加（スポーンイベント購読） ---------------
        SubscribeSpawnerEvents(true);
        // --------------- ここまで実装を追加 ---------------
    }

    private void OnDisable()
    {
        if (HintRef) HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        _input.Player.Disable();

        // --------------- ここから実装を追加（スポーンイベント購読解除） ---------------
        SubscribeSpawnerEvents(false);
        // --------------- ここまで実装を追加 ---------------
    }

    private void Start()
    {
        InitBottomTextHidden();
        ApplyDoorEnableByProgress(GetProgressSafely());

        if (AutoDisableSpawnersOnStart) SetSpawnersActive(false); // 敵はまだ出ない

        Step1();
    }

    private void Update()
    {
        var p = GetProgressSafely();
        if (p != _lastAppliedProgress) ApplyDoorEnableByProgress(p);

        HandleLockedDoorTapFeedback(); // 進行0でドアを叩いたら Step2 を流す
    }

    // ===== 進行度→OpenDoor.enabled =====
    private void ApplyDoorEnableByProgress(int progress)
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
    private void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);
    private int GetProgressSafely() => HintRef ? HintRef.ProgressStage : 0;

    // ===== ロック中ドアの“叩いた”検知 → Step2 再生 =====
    private void HandleLockedDoorTapFeedback()
    {
        if (!Player) return;
        if (_doorMsgCD > 0f) { _doorMsgCD -= Time.deltaTime; return; }
        if (!WasDoorPressThisFrame()) return;

        if (ExistsLockedDoorNearPlayer())
        {
            Step2();                        // ここで Step2 を再生
            _doorMsgCD = DoorLockedCooldown;
        }
    }

    private bool WasDoorPressThisFrame()
    {
        return
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();
    }

    private bool ExistsLockedDoorNearPlayer()
    {
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od || od.enabled) continue;

            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance)
                continue;

            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                if (Vector3.Dot(od.transform.forward, toPlayer) < DoorFacingDotThreshold)
                    continue;
            }
            return true;
        }
        return false;
    }

    // ===== テキストAPI =====
    public void Step1() => RestartTyping(CoTypeLines(Step1Lines));

    public void Step2()
    {
        if (_isStep2Playing) return;          // 多重再生防止
        _isStep2Playing = true;
        RestartTyping(CoTypeLines(Step2Lines));
    }

    public void Step3() => RestartTyping(CoTypeLines(Step3Lines)); // スポーン通知で呼ばれる

    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        RestartTyping(CoTypeOneShot(line));
    }

    private void InitBottomTextHidden()
    {
        if (!BottomText) return;
        BottomText.text = "";
        BottomText.gameObject.SetActive(false);
    }

    private void RestartTyping(IEnumerator routine)
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(routine);
    }

    private IEnumerator CoTypeOneShot(string line)
    {
        yield return CoTypeOne(line);
        yield return new WaitForSeconds(LineInterval);
        BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    private IEnumerator CoTypeLines(string[] lines)
    {
        if (!BottomText || lines == null || lines.Length == 0) yield break;

        for (int li = 0; li < lines.Length; li++)
        {
            yield return CoTypeOne(lines[li]);

            if (li < lines.Length - 1)
            {
                yield return new WaitForSeconds(LineInterval);
            }
            else if (ApplyIntervalAfterLastLine)
            {
                yield return new WaitForSeconds(LineInterval);
            }
        }

        if (HideWhenDone) BottomText.gameObject.SetActive(false);
        _typing = null;

        // Step2 を出し切った直後：テキストが消えた“あと”に少し待ってスポーン解放
        if (_isStep2Playing && !_spawnerUnlocked)
        {
            _isStep2Playing = false;
            StartCoroutine(CoEnableSpawnersAfterDelay(EnableSpawnerDelayAfterStep2));
        }
    }

    private IEnumerator CoTypeOne(string text)
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
                acc -= interval;
                i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    // ===== スポーン制御 =====
    private void SetSpawnersActive(bool active)
    {
        for (int i = 0; i < SpawnScripts.Count; i++)
        {
            var s = SpawnScripts[i];
            if (s) s.enabled = active;
        }
    }

    private IEnumerator CoEnableSpawnersAfterDelay(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay));
        SetSpawnersActive(true);
        _spawnerUnlocked = true; // 解放完了（Step3 はスポーンイベントで発火）
    }

    // --------------- ここから実装を追加（スポーン通知購読） ---------------
    private void SubscribeSpawnerEvents(bool subscribe)
    {
        if (Spawners == null) return;
        for (int i = 0; i < Spawners.Count; i++)
        {
            var sp = Spawners[i];
            if (!sp) continue;
            if (subscribe) sp.OnGhostSpawned.AddListener(OnGhostSpawned);
            else sp.OnGhostSpawned.RemoveListener(OnGhostSpawned);
        }
    }

    private void OnGhostSpawned()
    {
        if (_step3Fired) return;  // 一度だけ
        _step3Fired = true;
        Step3();                  // ★ 幽霊スポーンの瞬間にStep3を開始
    }
    // --------------- ここまで実装を追加 ---------------
}
