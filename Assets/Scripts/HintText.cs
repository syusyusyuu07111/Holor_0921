using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Events;

public class HintText : MonoBehaviour
{
    public Transform Player;                               // プレイヤー
    public Transform Ghost;                                // ゴースト中心（自動追尾で上書きされることあり）
    public SearchChase ChaseRef;                           // ゴースト状態(1/2)
    public HideCroset HideRef;                             // 隠れ状態（任意）

    // --------------- ここから追加（クローン追尾設定） ---------------
    [Header("ゴースト自動追尾")]
    public bool AutoTrackNearestGhost = true;              // 最寄りのゴースト（クローン含む）を自動で追尾
    public string GhostTag = "Ghost";                      // ゴーストに付けるTag
    public float RetargetInterval = 0.3f;                  // 追尾先の再検索間隔（秒）
    public bool AutoDeriveChaseRefFromGhost = true;        // 追尾先の SearchChase を自動で拾う
    private float _retargetTimer = 0f;                     // 検索タイマー
    private Transform _lastGhost;                          // 直前の追尾対象
    // --------------- ここまで追加 -----------------------------------

    // --------------- 表示（UI/3D 両対応） ---------------
    public TMP_Text[] HintLabels = new TMP_Text[5];        // UIでも3DでもOK
    public Canvas UICanvas;                                 // Screen Space の Canvas
    public bool ScreenSpaceUI = true;                       // trueならUI座標で配置

    // --------------- 進行管理 ---------------
    [System.Serializable] public class HintSet { [TextArea] public string[] State1 = new string[5]; [TextArea] public string[] State2 = new string[5]; }
    public List<HintSet> Stages = new List<HintSet>();     // ステージごとのヒント
    public int ProgressStage = 0;                           // 初期0

    // 進行イベント（任意）
    public UnityEvent<int> OnProgressChanged;              // 引数＝新しい ProgressStage

    // --------------- 距離/開示 ---------------
    public float VisibleDistance = 10f;                     // 伏字で見え始める距離
    public float RevealDistance = 7f;                       // 開示が進む距離
    public float RevealCharsPerSecond = 6f;                 // 秒あたり開示文字数
    public char MaskChar = '■';                             // 伏字文字

    // --------------- ヒント間クールタイム（追加） ---------------
    public float NextHintCooldown = 1.0f;                   // 次の行が開示を開始するまでの待機（秒）

    // --------------- 自動進行（全部開示後） ---------------
    public bool AutoAdvanceWhenAllRevealed = true;          // 5行すべて開いたら自動で次へ
    public float AutoAdvanceDelay = 1.0f;                   // 全開示から次へ進むまでの待機
    private float _autoAdvanceTimer = -1f;                  // <0 は未カウント

    // --------------- 配置演出 ---------------
    public float RingRadius = 1.8f;
    public float OrbitSpeed = 20f;
    public float BobAmplitude = 0.15f;
    public float BobSpeed = 2.0f;
    public float HeightOffset = 1.6f;

    // --------------- 画面に映っている時だけ表示する設定 ---------------
    public bool OnlyWhenGhostOnScreen = true;
    public float OnScreenMargin = 0.05f;
    public bool CheckOcclusion = false;
    public LayerMask Occluders;
    public float CameraEyeHeight = 0.0f;

    // --------------- 内部 ---------------
    private string[] activeLines = new string[5];
    private int currentIndex = 0;                           // 今開示中の行
    private float revealProgressChars = 0f;                 // 現行の開示文字数
    private bool waitingCooldown = false;                   // クールタイム中か
    private float cooldownTimer = 0f;                       // 残りクールタイム
    private int cachedState = -1, cachedStage = -1;

    void Start()
    {
        ProgressStage = Mathf.Max(0, ProgressStage);       // 初期は0
        SelectLinesByStageAndState();                      // 文言選択
        ApplyMaskedAll();                                  // 全伏字で初期化
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
    }

    void Update()
    {
        // 追尾：最寄りのゴースト（クローンを含む）を定期的に選び直す --------------------
        if (AutoTrackNearestGhost)
        {
            _retargetTimer -= Time.deltaTime;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = RetargetInterval;
                var newGhost = FindNearestGhostByTag();
                if (newGhost != _lastGhost)
                {
                    Ghost = newGhost;
                    _lastGhost = newGhost;

                    if (AutoDeriveChaseRefFromGhost)
                        ChaseRef = Ghost ? Ghost.GetComponent<SearchChase>() : null;

                    // 追尾対象が変わったら表示リセット（自然に切り替わる）
                    currentIndex = 0;
                    revealProgressChars = 0f;
                    waitingCooldown = false;
                    cooldownTimer = 0f;
                    _autoAdvanceTimer = -1f;
                    ApplyMaskedAll();
                    // 状態(1/2)の変化反映
                    SelectLinesByStageAndState();
                }
            }
        }

        if (!Player || !Ghost)
        {                           // ゴースト不在 → 非表示にして帰る
            for (int i = 0; i < HintLabels.Length; i++)
                if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
            return;
        }

        CheckAndMaybeAdvanceProgress();                    // 進行条件
        SelectLinesByStageAndState();                      // 状態/ステージ変化に追随

        float dist = Vector3.Distance(Player.position, Ghost.position);
        bool visibleByDistance = dist <= VisibleDistance;
        bool visibleByCamera = !OnlyWhenGhostOnScreen || IsGhostOnScreen();
        bool show = visibleByDistance && visibleByCamera;

        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(show);
        if (!show) return;

        AnimateRingLayout();                               // 配置更新

        // ---- 開示進行：クールタイムを考慮 ---------------------------------------
        if (dist <= RevealDistance && currentIndex < 5)
        {
            if (waitingCooldown)
            {
                cooldownTimer -= Time.deltaTime;           // クールタイム消化
                if (cooldownTimer <= 0f) waitingCooldown = false; // 終了で次行開始
            }
            else
            {
                // 通常の開示
                revealProgressChars += RevealCharsPerSecond * Time.deltaTime;
                UpdateMaskedLine(currentIndex, revealProgressChars);

                if (IsFullyRevealed(activeLines[currentIndex], revealProgressChars))
                {
                    // 行を開き切った → 次の行へ移る前にクールタイム
                    currentIndex = Mathf.Min(currentIndex + 1, 4);
                    revealProgressChars = 0f;
                    waitingCooldown = true;                // クールタイム開始
                    cooldownTimer = Mathf.Max(0f, NextHintCooldown);
                }
            }
        }

        // 行表示の整え：開示済/未着手/進行中 ---------------------------------------
        for (int i = 0; i < 5; i++)
        {
            if (!HintLabels[i]) continue;

            if (i < currentIndex) HintLabels[i].text = activeLines[i]; // 完全開示
            else if (i == currentIndex && !waitingCooldown) { /* UpdateMaskedLineで反映 */ }
            else HintLabels[i].text = MaskAll(activeLines[i]);         // 未着手やCT中は全伏字
        }
    }

    // --------------- ここから追加（最寄りゴースト検索） ---------------
    private Transform FindNearestGhostByTag()
    {
        if (string.IsNullOrEmpty(GhostTag) || !Player) return Ghost;

        // Tag で全取得（非推奨ではない）
        var gos = GameObject.FindGameObjectsWithTag(GhostTag);
        if (gos == null || gos.Length == 0) return null;

        Transform best = null;
        float bestSqr = float.MaxValue;
        Vector3 p = Player.position;

        for (int i = 0; i < gos.Length; i++)
        {
            var t = gos[i]?.transform;
            if (!t) continue;
            float d2 = (t.position - p).sqrMagnitude;
            if (d2 < bestSqr)
            {
                bestSqr = d2;
                best = t;
            }
        }
        return best;
    }
    // --------------- ここまで追加 -------------------------------------

    // --------------- カメラに映っているか ---------------
    private bool IsGhostOnScreen()
    {
        Camera cam = Camera.main;
        if (!cam) return true;
        Vector3 worldPos = Ghost.position + Vector3.up * HeightOffset;
        Vector3 vp = cam.WorldToViewportPoint(worldPos);
        if (vp.z <= 0f) return false;
        if (vp.x < -OnScreenMargin || vp.x > 1f + OnScreenMargin) return false;
        if (vp.y < -OnScreenMargin || vp.y > 1f + OnScreenMargin) return false;

        if (CheckOcclusion)
        {
            Vector3 camEye = cam.transform.position + Vector3.up * CameraEyeHeight;
            if (Physics.Linecast(camEye, worldPos, out RaycastHit hit, Occluders)) return false;
        }
        return true;
    }

    // --------------- ステージ＆状態で文言を選択 ---------------
    private void SelectLinesByStageAndState()
    {
        int state = (ChaseRef ? ChaseRef.GetState() : 1);
        if (Stages == null || Stages.Count == 0) { EnsureActiveEmpty(); return; }

        int stage = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stage];
        var source = (state == 2) ? set.State2 : set.State1;

        if (cachedState == state && cachedStage == stage && IsSameLines(activeLines, source)) return;

        for (int i = 0; i < 5; i++)
            activeLines[i] = (source != null && i < source.Length && !string.IsNullOrEmpty(source[i])) ? source[i] : "";

        // 文言が変わったら最初から
        currentIndex = 0;
        revealProgressChars = 0f;
        waitingCooldown = false;
        cooldownTimer = 0f;
        _autoAdvanceTimer = -1f;

        ApplyMaskedAll();

        cachedState = state;
        cachedStage = stage;
    }

    private bool IsSameLines(string[] a, string[] b)
    {
        if (a == null || b == null) return false;
        for (int i = 0; i < 5; i++)
        {
            var aa = (i < a.Length) ? a[i] : null;
            var bb = (i < b.Length) ? b[i] : null;
            if (aa != bb) return false;
        }
        return true;
    }

    private void EnsureActiveEmpty() { for (int i = 0; i < 5; i++) activeLines[i] = ""; }

    // --------------- 進行条件（自動進行） ---------------
    private void CheckAndMaybeAdvanceProgress()
    {
        if (!AutoAdvanceWhenAllRevealed) return;
        if (!AllFiveRevealed()) { _autoAdvanceTimer = -1f; return; }

        if (_autoAdvanceTimer < 0f) _autoAdvanceTimer = AutoAdvanceDelay;      // カウント開始
        else
        {
            _autoAdvanceTimer -= Time.deltaTime;
            if (_autoAdvanceTimer <= 0f)
            {
                _autoAdvanceTimer = -1f;
                AdvanceProgress();
            }
        }
    }

    public void AdvanceProgress() { SetProgress(ProgressStage + 1); }

    public void SetProgress(int next)
    {
        int clamped = Mathf.Clamp(next, 0, Mathf.Max(0, (Stages?.Count ?? 1) - 1));
        if (clamped == ProgressStage) return;
        ProgressStage = clamped;

        currentIndex = 0; revealProgressChars = 0f;
        waitingCooldown = false; cooldownTimer = 0f;
        _autoAdvanceTimer = -1f;

        SelectLinesByStageAndState();
        OnProgressChanged?.Invoke(ProgressStage);          // 外部へ通知（任意）
    }

    // ---- 表示ユーティリティ ------------------------------------------------------
    private void ApplyMaskedAll()
    {
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].text = MaskAll(i < activeLines.Length ? activeLines[i] : "");
    }
    private void UpdateMaskedLine(int index, float revealedChars)
    {
        if (index < 0 || index >= activeLines.Length) return;
        if (!HintLabels[index]) return;
        string src = activeLines[index];
        int count = Mathf.Clamp(Mathf.FloorToInt(revealedChars), 0, src.Length);
        HintLabels[index].text = RevealLeftToRight(src, count);
    }
    private string MaskAll(string s) { return string.IsNullOrEmpty(s) ? "" : new string(MaskChar, s.Length); }
    private string RevealLeftToRight(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        n = Mathf.Clamp(n, 0, s.Length);
        return s.Substring(0, n) + new string(MaskChar, s.Length - n);
    }
    private bool IsFullyRevealed(string s, float revealedChars) { return Mathf.FloorToInt(revealedChars) >= (s?.Length ?? 0); }
    private bool AllFiveRevealed()
    {
        if (currentIndex < 4) return false;
        return IsFullyRevealed(activeLines[4], revealProgressChars) || string.IsNullOrEmpty(activeLines[4]);
    }

    // ---- リング配置（UI/3D 切替） ------------------------------------------------
    private void AnimateRingLayout()
    {
        float t = Time.time;
        Camera cam = Camera.main;

        for (int i = 0; i < HintLabels.Length; i++)
        {
            var label = HintLabels[i];
            if (!label) continue;

            float angleDeg = (360f / Mathf.Max(1, HintLabels.Length)) * i + t * OrbitSpeed;
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 around = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * RingRadius;
            float bob = Mathf.Sin(t * BobSpeed + i * 0.6f) * BobAmplitude;

            Vector3 worldPos = Ghost.position + around + Vector3.up * (HeightOffset + bob);

            if (ScreenSpaceUI && UICanvas)
            {
                Vector3 screen = cam ? cam.WorldToScreenPoint(worldPos) : worldPos;
                (label.transform as RectTransform).position = screen;
            }
            else
            {
                label.transform.position = worldPos;
                if (cam) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Ghost) return;
        Gizmos.color = Color.white; Gizmos.DrawWireSphere(Ghost.position, VisibleDistance);
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(Ghost.position, RevealDistance);
    }
}
