using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Events;

public class HintText : MonoBehaviour
{
    public Transform Player;
    public Transform Ghost;
    public SearchChase ChaseRef;
    public HideCroset HideRef;

    [Header("初見イベント")]
    public UnityEvent OnFirstGhostSeen;
    public UnityEvent OnFirstState2Seen;
    public UnityEvent<int> OnProgressChanged;

    [Header("ゴースト自動追尾")]
    public bool AutoTrackNearestGhost = true;
    public string GhostTag = "Ghost";
    public float RetargetInterval = 0.3f;
    public bool AutoDeriveChaseRefFromGhost = true;
    private float _retargetTimer = 0f;
    private Transform _lastGhost;

    [Header("表示")]
    public TMP_Text[] HintLabels = new TMP_Text[5];
    public Canvas UICanvas;
    public bool ScreenSpaceUI = true;

    // 見つかった時の一括上書き（即・全開示）
    [Header("見つかった時の上書き")]
    [TextArea] public string FoundOverrideText = "絶対見つける";
    public bool EnableFoundOverride = true;
    public bool FoundInstantReveal = true;

    // 色設定（通常時の全体デフォルト）
    [Header("色設定（通常時・デフォルト）")]
    public Color[] LineColors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

    [Header("色設定（見つかった時）")]
    public bool UseFoundSingleColor = true;    // true: 全行同色
    public Color FoundOverrideColor = Color.red;
    public bool UseFoundPerLineColors = false; // true: 行ごとの色を使う
    public Color[] FoundLineColors = new Color[5] { Color.red, Color.red, Color.red, Color.red, Color.red };

    // ▼ ステージごとに「ステート別の色」を持てるよう拡張
    [System.Serializable]
    public class HintSet
    {
        [Header("State 1")]
        [TextArea] public string[] State1 = new string[5];
        [Tooltip("State1 の各行に対応する色（未設定は白→全体デフォルトへフォールバック）")]
        public Color[] State1Colors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

        [Header("State 2")]
        [TextArea] public string[] State2 = new string[5];
        [Tooltip("State2 の各行に対応する色（未設定は白→全体デフォルトへフォールバック）")]
        public Color[] State2Colors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

        [Header("State 3（任意）")]
        [TextArea] public string[] State3 = new string[5];
        [Tooltip("State3 の各行に対応する色（未設定は白→全体デフォルトへフォールバック）")]
        public Color[] State3Colors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };
    }

    public List<HintSet> Stages = new List<HintSet>();
    public int ProgressStage = 0;

    [Header("開示ルール")]
    public float VisibleDistance = 10f;
    public float RevealDistance = 7f;
    public float RevealCharsPerSecond = 6f;
    public char MaskChar = '■';

    [Header("行間クールタイム")]
    public float NextHintCooldown = 1.0f;

    [Header("自動進行（5行すべて開示後）")]
    public bool AutoAdvanceWhenAllRevealed = true;
    public float AutoAdvanceDelay = 1.0f;
    private float _autoAdvanceTimer = -1f;

    [Header("レイアウト（リング）")]
    public float RingRadius = 1.8f;
    public float OrbitSpeed = 20f;
    public float BobAmplitude = 0.15f;
    public float BobSpeed = 2.0f;
    public float HeightOffset = 1.6f;

    [Header("画面内チェック")]
    public bool OnlyWhenGhostOnScreen = true;
    public float OnScreenMargin = 0.05f;
    public bool CheckOcclusion = false;
    public LayerMask Occluders;
    public float CameraEyeHeight = 0.0f;

    // ---- 内部 ----
    private string[] activeLines = new string[5];
    private int currentIndex = 0;
    private float revealProgressChars = 0f;
    private bool waitingCooldown = false;
    private float cooldownTimer = 0f;
    private int cachedState = -1, cachedStage = -1;

    private bool _seenAnyOnce = false;
    private bool _seenState2Once = false;
    private bool _visiblePrev = false;

    private bool _foundOverrideActive = false; // “見つかった用”テキスト差し替え中か
    private bool _foundPrev = false;           // false→true / true→false 検出用

    // 現在の「ステージ×ステート」に対応する色配列参照（通常時のみ使用）
    private Color[] _activeStateColors = null;

    void Start()
    {
        // 配列の長さケア
        EnsureColorArraySize(ref LineColors, 5, Color.white);
        EnsureColorArraySize(ref FoundLineColors, 5, Color.red);

        ProgressStage = Mathf.Max(0, ProgressStage);
        SelectLinesByStageAndState();
        ApplyMaskedAll();

        // 初期色（通常色）を一括適用
        ApplyTextColorsProfile(foundActive: false);

        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
    }

    void Update()
    {
        // ====== 追尾 ======
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

                    ResetRevealProgress();
                    ApplyMaskedAll();
                    SelectLinesByStageAndState();
                }
            }
        }

        if (!Player || !Ghost)
        {
            for (int i = 0; i < HintLabels.Length; i++)
                if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);

            _visiblePrev = false;

            if (_foundOverrideActive) ClearFoundOverride();
            if (_foundPrev) { _foundPrev = false; ApplyTextColorsProfile(foundActive: false); }
            return;
        }

        // ====== 可視判定 ======
        float dist = Vector3.Distance(Player.position, Ghost.position);
        bool visibleByDistance = dist <= VisibleDistance;
        bool onScreen = !OnlyWhenGhostOnScreen || IsGhostOnScreen();
        bool visible = visibleByDistance && onScreen;

        bool isHiding = (HideRef && HideRef.hide);

        if (visible && !_visiblePrev && !isHiding)
        {
            if (!_seenAnyOnce) { _seenAnyOnce = true; OnFirstGhostSeen?.Invoke(); }
            int st0 = (ChaseRef ? ChaseRef.GetState() : 1);
            if (st0 == 2 && !_seenState2Once) { _seenState2Once = true; OnFirstState2Seen?.Invoke(); }
        }
        _visiblePrev = visible;

        // ====== 文言選択（通常） ======
        CheckAndMaybeAdvanceProgress();
        SelectLinesByStageAndState();

        // ====== 見つかっているか？ → テキスト上書き／解除 ======
        bool found = (ChaseRef && ChaseRef.isDiscovery);
        if (EnableFoundOverride)
        {
            if (found && !_foundOverrideActive) ApplyFoundOverrideInstant();
            else if (!found && _foundOverrideActive) ClearFoundOverride();
        }

        // 色切替（状態変化の瞬間のみ）
        if (found != _foundPrev)
        {
            ApplyTextColorsProfile(foundActive: found);
        }
        _foundPrev = found;

        // 表示フラグ
        bool show = visible;
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(show);
        if (!show) return;

        // レイアウト
        AnimateRingLayout();

        // ====== 表示更新 ======
        if (_foundOverrideActive)
        {
            // 見つかってる間は即・全開示＆常に同じテキスト（色は切替済み）
            for (int i = 0; i < 5; i++)
                if (HintLabels[i]) HintLabels[i].text = activeLines[i];
            return;
        }

        // ====== 通常の文字開示 ======
        if (dist <= RevealDistance && currentIndex < 5)
        {
            if (waitingCooldown)
            {
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f) waitingCooldown = false;
            }
            else
            {
                revealProgressChars += RevealCharsPerSecond * Time.deltaTime;
                UpdateMaskedLine(currentIndex, revealProgressChars);

                if (IsFullyRevealed(activeLines[currentIndex], revealProgressChars))
                {
                    currentIndex = Mathf.Min(currentIndex + 1, 4);
                    revealProgressChars = 0f;
                    waitingCooldown = true;
                    cooldownTimer = Mathf.Max(0f, NextHintCooldown);
                }
            }
        }

        // 行の最終反映（通常時）
        for (int i = 0; i < 5; i++)
        {
            if (!HintLabels[i]) continue;

            if (i < currentIndex) HintLabels[i].text = activeLines[i];  // 完全開示
            else if (i == currentIndex && !waitingCooldown)
            {
                // 進行中は UpdateMaskedLine が反映済み
            }
            else
            {
                HintLabels[i].text = MaskAll(activeLines[i]);            // 未着手 or CT中
            }
        }
    }

    // ====== 近いゴーストを探す ======
    private Transform FindNearestGhostByTag()
    {
        if (string.IsNullOrEmpty(GhostTag) || !Player) return Ghost;

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
            if (d2 < bestSqr) { bestSqr = d2; best = t; }
        }
        return best;
    }

    // ====== 画面内チェック ======
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

    // ====== ステージ＆状態で文言を選択 + 色配列の選択 ======
    private void SelectLinesByStageAndState()
    {
        int state = (ChaseRef ? ChaseRef.GetState() : 1);
        if (Stages == null || Stages.Count == 0) { EnsureActiveEmpty(); _activeStateColors = null; return; }

        int stage = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stage];

        // 安全化：各色配列の長さ
        if (set != null)
        {
            EnsureColorArraySize(ref set.State1Colors, 5, Color.white);
            EnsureColorArraySize(ref set.State2Colors, 5, Color.white);
            EnsureColorArraySize(ref set.State3Colors, 5, Color.white);
        }

        string[] source = null;
        Color[] stateColors = null;

        switch (state)
        {
            case 1:
                source = set.State1;
                stateColors = set.State1Colors;
                break;
            case 2:
                source = set.State2;
                stateColors = set.State2Colors;
                break;
            case 3: // 任意
                source = (set.State3 != null && set.State3.Length > 0) ? set.State3
                       : (set.State2 != null && set.State2.Length > 0) ? set.State2
                       : set.State1;
                stateColors = set.State3Colors;
                break;
            default:
                source = set.State1;
                stateColors = set.State1Colors;
                break;
        }

        if (!_foundOverrideActive)
        {
            if (cachedState == state && cachedStage == stage && IsSameLines(activeLines, source)) return;

            for (int i = 0; i < 5; i++)
                activeLines[i] = (source != null && i < source.Length && !string.IsNullOrEmpty(source[i])) ? source[i] : "";

            _activeStateColors = stateColors; // ← このステートの色を通常時に使う

            ResetRevealProgress();
            ApplyMaskedAll();

            cachedState = state;
            cachedStage = stage;

            // state/ステージ切替時に色も即反映
            ApplyTextColorsProfile(foundActive: _foundPrev);
        }
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

    // ====== 進行（自動） ======
    private void CheckAndMaybeAdvanceProgress()
    {
        if (_foundOverrideActive) return;

        if (!AutoAdvanceWhenAllRevealed) return;
        if (!AllFiveRevealed()) { _autoAdvanceTimer = -1f; return; }

        if (_autoAdvanceTimer < 0f) _autoAdvanceTimer = AutoAdvanceDelay;
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

        ResetRevealProgress();
        SelectLinesByStageAndState();
        OnProgressChanged?.Invoke(ProgressStage);
    }

    // ====== 表示ユーティリティ ======
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

    // ====== リング配置 ======
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

    // ===== 上書き制御 =====
    private void ApplyFoundOverrideInstant()
    {
        _foundOverrideActive = true;

        // 5行すべてを“見つかった用テキスト”で統一
        for (int i = 0; i < 5; i++)
            activeLines[i] = FoundOverrideText ?? "";

        // 即・全開示
        if (FoundInstantReveal)
        {
            currentIndex = 4;
            revealProgressChars = (activeLines[4]?.Length ?? 0);
            waitingCooldown = false;
            cooldownTimer = 0f;
            _autoAdvanceTimer = -1f;

            for (int i = 0; i < 5; i++)
                if (HintLabels[i]) HintLabels[i].text = activeLines[i];
        }
        else
        {
            ResetRevealProgress();
            ApplyMaskedAll();
        }
    }

    private void ClearFoundOverride()
    {
        _foundOverrideActive = false;

        // 元のステージ/状態に基づく文言へ戻す
        SelectLinesByStageAndState();

        // 通常の開示アニメへ復帰
        ResetRevealProgress();
        ApplyMaskedAll();
    }

    private void ResetRevealProgress()
    {
        currentIndex = 0;
        revealProgressChars = 0f;
        waitingCooldown = false;
        cooldownTimer = 0f;
        _autoAdvanceTimer = -1f;
    }

    // ===== 色適用まわり =====
    private void ApplyTextColorsProfile(bool foundActive)
    {
        if (HintLabels == null) return;

        if (foundActive)
        {
            if (UseFoundSingleColor)
            {
                for (int i = 0; i < HintLabels.Length; i++)
                    if (HintLabels[i]) HintLabels[i].color = FoundOverrideColor;
            }
            else if (UseFoundPerLineColors)
            {
                EnsureColorArraySize(ref FoundLineColors, 5, Color.red);
                for (int i = 0; i < HintLabels.Length; i++)
                    if (HintLabels[i]) HintLabels[i].color = FoundLineColors[Mathf.Clamp(i, 0, FoundLineColors.Length - 1)];
            }
            else
            {
                // 切替しない → 通常色（= 下のブランチに委ねる）
                ApplyLineColors(LineColors);
            }
        }
        else
        {
            // 通常：現在の「ステージ×ステート」の色配列があれば優先、なければ全体デフォルト
            if (_activeStateColors != null)
            {
                EnsureColorArraySize(ref _activeStateColors, 5, Color.white);
                for (int i = 0; i < HintLabels.Length; i++)
                    if (HintLabels[i]) HintLabels[i].color = _activeStateColors[Mathf.Clamp(i, 0, _activeStateColors.Length - 1)];
            }
            else
            {
                ApplyLineColors(LineColors);
            }
        }
    }

    private void ApplyLineColors(Color[] colors)
    {
        EnsureColorArraySize(ref colors, 5, Color.white);
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].color = colors[Mathf.Clamp(i, 0, colors.Length - 1)];
    }

    private void EnsureColorArraySize(ref Color[] arr, int need, Color fill)
    {
        if (arr == null || arr.Length != need)
        {
            var newArr = new Color[need];
            for (int i = 0; i < need; i++)
                newArr[i] = (arr != null && i < arr.Length) ? arr[i] : fill;
            arr = newArr;
        }
    }
}