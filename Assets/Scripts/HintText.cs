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

    [Header("2つ目の部屋フラグ参照")]
    public SecondRoomTutorial SecondRoomRef;   // 2部屋目かどうかを見る

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

    [Header("見つかった時の上書き")]
    [TextArea] public string FoundOverrideText = "絶対見つける";
    public bool EnableFoundOverride = true;
    public bool FoundInstantReveal = true;

    [Header("色設定（通常）")]
    public Color[] LineColors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

    [Header("色設定（見つかった時）")]
    public bool UseFoundSingleColor = true;
    public Color FoundOverrideColor = Color.red;
    public bool UseFoundPerLineColors = false;
    public Color[] FoundLineColors = new Color[5] { Color.red, Color.red, Color.red, Color.red, Color.red };

    [System.Serializable]
    public class HintSet
    {
        [Header("State 1（1部屋目: 幽霊state1用）")]
        [TextArea] public string[] State1 = new string[5];
        public Color[] State1Colors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

        [Header("State 2（1部屋目: 幽霊state2用）")]
        [TextArea] public string[] State2 = new string[5];
        public Color[] State2Colors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

        [Header("State 3（2部屋目: 幽霊state1用）")]
        [TextArea] public string[] State3 = new string[5];
        public Color[] State3Colors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

        [Header("State 4（2部屋目: 幽霊state2用）")]
        [TextArea] public string[] State4 = new string[5];
        public Color[] State4Colors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

        [Header("全文開示時に送る台詞（任意）")]
        [TextArea] public string[] TutorialLinesOnFullyRevealed = new string[0];
    }

    [Header("ステージ")]
    public List<HintSet> Stages = new List<HintSet>();
    public int ProgressStage = 0;

    [System.Serializable]
    public class HintTutorialLinesEvent : UnityEvent<string[]> { }

    [Header("チュートリアル連携")]
    public HintTutorialLinesEvent OnHintTutorialLinesRequested = new HintTutorialLinesEvent();

    [Header("行開示トリガ")]
    public UnityEvent<string> OnLineFullyRevealed = new UnityEvent<string>();
    private readonly HashSet<string> _lineRevealedIds = new HashSet<string>(); // 一度きり
    public bool HasLineBeenRevealed(int state, int element) => _lineRevealedIds.Contains(MakeId(state, element));
    private static string MakeId(int state, int element) => $"state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";

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

    [Header("クローゼット中の特別表示")]
    public bool ForceVisibleWhileHiding = true;

    [Header("2部屋目ヒントミッション連携")]
    public UnityEvent OnSecondRoomHintsFullyRevealed = new UnityEvent();
    public UnityEvent<int, int> OnSecondRoomHintProgressChanged = new UnityEvent<int, int>();

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

    private bool _foundOverrideActive = false;
    private bool _foundPrev = false;

    private Color[] _activeStateColors = null;
    private readonly HashSet<int> _tutorialShownStages = new HashSet<int>();

    private bool _secondRoomHintMissionCompleted = false;
    private int _lastSecondRoomHave = -1;
    private int _lastSecondRoomNeed = -1;

    void Start()
    {
        EnsureColorArraySize(ref LineColors, 5, Color.white);
        EnsureColorArraySize(ref FoundLineColors, 5, Color.red);

        ProgressStage = Mathf.Max(0, ProgressStage);
        SelectLinesByStageAndState();
        ApplyMaskedAll();
        ApplyTextColorsProfile(false);

        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);

        CheckSecondRoomHintsMission();
    }

    void Update()
    {
        // ゴースト追尾（ゴースト差し替え時は状態を初期化）
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
            if (_foundPrev)
            {
                _foundPrev = false;
                ApplyTextColorsProfile(false);
            }

            return;
        }

        // 可視判定
        float dist = Vector3.Distance(Player.position, Ghost.position);
        bool visibleByDistance = dist <= VisibleDistance;
        bool onScreen = !OnlyWhenGhostOnScreen || IsGhostOnScreen();
        bool isHiding = (HideRef && HideRef.hide);

        bool visible = visibleByDistance && onScreen;
        bool show = visible || (isHiding && ForceVisibleWhileHiding);

        // 初見イベント
        if (visible && !_visiblePrev && !isHiding)
        {
            if (!_seenAnyOnce)
            {
                _seenAnyOnce = true;
                OnFirstGhostSeen?.Invoke();
            }

            int st0 = (ChaseRef ? ChaseRef.GetState() : 1);
            if (st0 == 2 && !_seenState2Once)
            {
                _seenState2Once = true;
                OnFirstState2Seen?.Invoke();
            }
        }
        _visiblePrev = visible;

        // ステージ進行＆文言選択
        CheckAndMaybeAdvanceProgress();
        SelectLinesByStageAndState();

        // 発見状態
        bool actuallyFound = (ChaseRef && ChaseRef.isDiscovery);
        bool treatAsFoundForDisplay = EnableFoundOverride && actuallyFound && !isHiding;

        if (treatAsFoundForDisplay)
        {
            if (!_foundOverrideActive) ApplyFoundOverrideInstant();
        }
        else
        {
            if (_foundOverrideActive) ClearFoundOverride();
        }

        if (treatAsFoundForDisplay != _foundPrev)
            ApplyTextColorsProfile(treatAsFoundForDisplay);
        _foundPrev = treatAsFoundForDisplay;

        // 表示切替
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(show);
        if (!show)
        {
            CheckSecondRoomHintsMission();
            return;
        }

        // レイアウト
        AnimateRingLayout();

        // 発見上書き中はそのテキストを出す
        if (_foundOverrideActive)
        {
            for (int i = 0; i < 5; i++)
                if (HintLabels[i]) HintLabels[i].text = activeLines[i];

            CheckSecondRoomHintsMission();
            return;
        }

        // 通常の文字開示
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
                    // ★ 行単位イベント
                    int ghostState = (ChaseRef ? ChaseRef.GetState() : 1);
                    int hintState = ConvertGhostStateToHintState(ghostState);
                    string id = MakeId(hintState, currentIndex);

                    if (_lineRevealedIds.Add(id))
                    {
                        Debug.Log($"[HintText] OnLineFullyRevealed -> {id}");
                        OnLineFullyRevealed?.Invoke(id);

                        // 2部屋目ヒントミッション進捗チェック
                        CheckSecondRoomHintsMission();
                    }

                    currentIndex = Mathf.Min(currentIndex + 1, 4);
                    revealProgressChars = 0f;
                    waitingCooldown = true;
                    cooldownTimer = Mathf.Max(0f, NextHintCooldown);
                }
            }
        }

        // 行の最終反映
        for (int i = 0; i < 5; i++)
        {
            if (!HintLabels[i]) continue;

            if (i < currentIndex)
            {
                HintLabels[i].text = activeLines[i];
            }
            else if (i == currentIndex && !waitingCooldown)
            {
                // 進行中は UpdateMaskedLine が反映済み
            }
            else
            {
                HintLabels[i].text = MaskAll(activeLines[i]);
            }
        }

        CheckSecondRoomHintsMission();
    }

    // 近いゴーストを探す
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

    // 画面内チェック
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
            if (Physics.Linecast(camEye, worldPos, out RaycastHit hit, Occluders))
                return false;
        }

        return true;
    }

    // ゴースト state → ヒント state(1〜4) 変換
    private int ConvertGhostStateToHintState(int ghostState)
    {
        bool inSecondRoom = (SecondRoomRef != null && SecondRoomRef.IsPlayerInSecondRoom);

        if (!inSecondRoom)
        {
            // 1部屋目： そのまま 1 or 2
            return ghostState;
        }
        else
        {
            // 2部屋目： 1→3, 2→4
            if (ghostState == 1) return 3;
            if (ghostState == 2) return 4;
            return ghostState;
        }
    }

    // ステージ＆状態で文言 + 色選択
    private void SelectLinesByStageAndState()
    {
        int ghostState = (ChaseRef ? ChaseRef.GetState() : 1);
        int hintState = ConvertGhostStateToHintState(ghostState);

        if (Stages == null || Stages.Count == 0)
        {
            EnsureActiveEmpty();
            _activeStateColors = null;
            return;
        }

        int stage = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stage];

        if (set != null)
        {
            EnsureColorArraySize(ref set.State1Colors, 5, Color.white);
            EnsureColorArraySize(ref set.State2Colors, 5, Color.white);
            EnsureColorArraySize(ref set.State3Colors, 5, Color.white);
            EnsureColorArraySize(ref set.State4Colors, 5, Color.white);
        }

        string[] source = null;
        Color[] stateColors = null;

        switch (hintState)
        {
            case 1:
                source = set.State1;
                stateColors = set.State1Colors;
                break;
            case 2:
                source = set.State2;
                stateColors = set.State2Colors;
                break;
            case 3:
                source = set.State3;
                stateColors = set.State3Colors;
                break;
            case 4:
                source = set.State4;
                stateColors = set.State4Colors;
                break;
            default:
                source = set.State1;
                stateColors = set.State1Colors;
                break;
        }

        if (!_foundOverrideActive)
        {
            if (cachedState == hintState && cachedStage == stage && IsSameLines(activeLines, source))
                return;

            for (int i = 0; i < 5; i++)
            {
                activeLines[i] =
                    (source != null && i < source.Length && !string.IsNullOrEmpty(source[i]))
                    ? source[i]
                    : "";
            }

            _activeStateColors = stateColors;

            ResetRevealProgress();
            ApplyMaskedAll();

            cachedState = hintState;
            cachedStage = stage;

            ApplyTextColorsProfile(_foundPrev);
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

    private void EnsureActiveEmpty()
    {
        for (int i = 0; i < 5; i++) activeLines[i] = "";
    }

    // 進行（自動）
    private void CheckAndMaybeAdvanceProgress()
    {
        if (_foundOverrideActive) return;

        bool allRevealed = AllFiveRevealed();
        if (allRevealed) TrySendTutorialLinesForStage();

        if (!AutoAdvanceWhenAllRevealed)
        {
            _autoAdvanceTimer = -1f;
            return;
        }

        if (!allRevealed)
        {
            _autoAdvanceTimer = -1f;
            return;
        }

        if (_autoAdvanceTimer < 0f)
        {
            _autoAdvanceTimer = AutoAdvanceDelay;
        }
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

    public void AdvanceProgress()
    {
        SetProgress(ProgressStage + 1);
    }

    public void SetProgress(int next)
    {
        int clamped = Mathf.Clamp(next, 0, Mathf.Max(0, (Stages?.Count ?? 1) - 1));
        if (clamped == ProgressStage) return;

        ProgressStage = clamped;

        ResetRevealProgress();
        SelectLinesByStageAndState();
        OnProgressChanged?.Invoke(ProgressStage);

        CheckSecondRoomHintsMission();
    }

    // 表示ユーティリティ
    private void ApplyMaskedAll()
    {
        for (int i = 0; i < HintLabels.Length; i++)
        {
            if (!HintLabels[i]) continue;

            string src = (i < activeLines.Length) ? activeLines[i] : "";
            HintLabels[i].text = MaskAll(src);
        }
    }

    private void UpdateMaskedLine(int index, float revealedChars)
    {
        if (index < 0 || index >= activeLines.Length) return;
        if (!HintLabels[index]) return;

        string src = activeLines[index];
        int count = Mathf.Clamp(Mathf.FloorToInt(revealProgressChars), 0, src.Length);
        HintLabels[index].text = RevealLeftToRight(src, count);
    }

    private string MaskAll(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : new string(MaskChar, s.Length);
    }

    private string RevealLeftToRight(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        n = Mathf.Clamp(n, 0, s.Length);
        return s.Substring(0, n) + new string(MaskChar, s.Length - n);
    }

    private bool IsFullyRevealed(string s, float revealedChars)
    {
        return Mathf.FloorToInt(revealProgressChars) >= (s?.Length ?? 0);
    }

    private bool AllFiveRevealed()
    {
        if (currentIndex < 4) return false;
        return IsFullyRevealed(activeLines[4], revealProgressChars) || string.IsNullOrEmpty(activeLines[4]);
    }

    private void TrySendTutorialLinesForStage()
    {
        if (_foundOverrideActive) return;
        if (Stages == null || Stages.Count == 0) return;

        int stageIndex = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stageIndex];
        if (set == null || set.TutorialLinesOnFullyRevealed == null) return;

        bool hasContent = false;
        for (int i = 0; i < set.TutorialLinesOnFullyRevealed.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(set.TutorialLinesOnFullyRevealed[i]))
            {
                hasContent = true;
                break;
            }
        }
        if (!hasContent) return;

        OnHintTutorialLinesRequested?.Invoke((string[])set.TutorialLinesOnFullyRevealed.Clone());
    }

    // ★ SecondRoomTutorial から 〇/〇 を取得する用の関数
    public void GetSecondRoomHintProgress(out int have, out int need)
    {
        have = 0;
        need = 0;

        if (SecondRoomRef == null || !SecondRoomRef.IsPlayerInSecondRoom)
            return;
        if (Stages == null || Stages.Count == 0)
            return;

        int stageIndex = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stageIndex];
        if (set == null) return;

        // state3 / state4 に実際にテキストが入っている行を合計
        for (int i = 0; i < 5; i++)
        {
            if (set.State3 != null && i < set.State3.Length && !string.IsNullOrEmpty(set.State3[i]))
                need++;
            if (set.State4 != null && i < set.State4.Length && !string.IsNullOrEmpty(set.State4[i]))
                need++;
        }
        if (need == 0) return;

        // そのうち解放済み（行が全部見えた）になっているもの
        for (int i = 0; i < 5; i++)
        {
            if (HasLineBeenRevealed(3, i) &&
                set.State3 != null && i < set.State3.Length && !string.IsNullOrEmpty(set.State3[i]))
                have++;

            if (HasLineBeenRevealed(4, i) &&
                set.State4 != null && i < set.State4.Length && !string.IsNullOrEmpty(set.State4[i]))
                have++;
        }
    }

    // 2部屋目用：state3/state4 の開放数カウント＆イベント
    private void CheckSecondRoomHintsMission()
    {
        if (SecondRoomRef == null || !SecondRoomRef.IsPlayerInSecondRoom)
            return;

        int have, need;
        GetSecondRoomHintProgress(out have, out need);
        if (need == 0) return;

        if (have != _lastSecondRoomHave || need != _lastSecondRoomNeed)
        {
            _lastSecondRoomHave = have;
            _lastSecondRoomNeed = need;

            Debug.Log($"[HintText] 2部屋目ヒント進捗 {have}/{need}");
            OnSecondRoomHintProgressChanged?.Invoke(have, need);
        }

        if (_secondRoomHintMissionCompleted)
            return;

        if (have >= need)
        {
            _secondRoomHintMissionCompleted = true;

            Debug.Log("[HintText] 2部屋目ヒント（state3/state4）が全て開示されました");
            OnSecondRoomHintsFullyRevealed?.Invoke();
        }
    }

    // リング配置
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
                if (cam)
                    label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Ghost) return;

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(Ghost.position, VisibleDistance);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(Ghost.position, RevealDistance);
    }

    // 上書き制御
    private void ApplyFoundOverrideInstant()
    {
        _foundOverrideActive = true;

        for (int i = 0; i < 5; i++)
            activeLines[i] = FoundOverrideText ?? "";

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
        SelectLinesByStageAndState();
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

    // 色適用
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
                ApplyLineColors(LineColors);
            }
        }
        else
        {
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
