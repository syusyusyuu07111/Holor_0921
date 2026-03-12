using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Events;

/*
     ゴースト周辺にリング状のヒントテキストを表示する
     ・距離/画面内判定で表示
     ・近づくと1行ずつマスク解除（全文表示）
     ・発見状態では上書き表示（任意）
     ・2部屋目(state3/state4)のヒント進捗をイベントで通知する

     2部屋目の表示ルール
     ・State3 / State4 のどちらか片方がすでに全部開示済みなら、未完了の方だけを出す
     ・両方とも未完了なら、State3 と State4 を交互に出す
     ・Inspector の参照や設定は既存のまま使えるようにしている
*/

public class HintText : MonoBehaviour
{
    //================
    // Inspector参照
    //================

    public Transform Player;
    public Transform Ghost;
    public SearchChase ChaseRef;
    public HideCroset HideRef;

    [Header("2つ目の部屋フラグ参照")]
    public SecondRoomTutorial SecondRoomRef;                     // 2部屋目かどうかを見る

    //================
    // 初見イベント
    //================

    [Header("初見イベント")]
    public UnityEvent OnFirstGhostSeen;
    public UnityEvent OnFirstState2Seen;
    public UnityEvent<int> OnProgressChanged;

    //================
    // 2部屋目ヒントミッション
    //================

    [Header("2部屋目ヒントミッション")]
    public UnityEvent<int, int> OnSecondRoomHintProgressUpdated; // (have, need)
    public UnityEvent OnSecondRoomHintsFullyRevealed;            // 2部屋目ヒント全部集め終わり
    private bool _secondRoomMissionCleared = false;

    //================
    // ゴースト自動追尾
    //================

    [Header("ゴースト自動追尾")]
    public bool AutoTrackNearestGhost = true;
    public string GhostTag = "Ghost";
    public float RetargetInterval = 0.3f;
    public bool AutoDeriveChaseRefFromGhost = true;
    private float _retargetTimer = 0f;
    private Transform _lastGhost;

    //================
    // UI表示
    //================

    [Header("表示")]
    public TMP_Text[] HintLabels = new TMP_Text[5];
    public Canvas UICanvas;
    public bool ScreenSpaceUI = true;

    //================
    // 発見時上書き
    //================

    [Header("見つかった時の上書き")]
    [TextArea] public string FoundOverrideText = "絶対見つける";
    public bool EnableFoundOverride = true;
    public bool FoundInstantReveal = true;

    //================
    // 色設定
    //================

    [Header("色設定（通常）")]
    public Color[] LineColors = new Color[5] { Color.white, Color.white, Color.white, Color.white, Color.white };

    [Header("色設定（見つかった時）")]
    public bool UseFoundSingleColor = true;
    public Color FoundOverrideColor = Color.red;
    public bool UseFoundPerLineColors = false;
    public Color[] FoundLineColors = new Color[5] { Color.red, Color.red, Color.red, Color.red, Color.red };

    //================
    // ステージ×ステート（文言テーブル）
    //================

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

        [Header("State3 全解放時につぶやくセリフ")]
        [TextArea] public string[] State3FullyRevealedLines = new string[0];

        [Header("State4 全解放時につぶやくセリフ")]
        [TextArea] public string[] State4FullyRevealedLines = new string[0];
    }

    [Header("ステージ")]
    public List<HintSet> Stages = new List<HintSet>();
    public int ProgressStage = 0;

    [System.Serializable]
    public class HintTutorialLinesEvent : UnityEvent<string[]> { }

    //================
    // チュートリアル連携
    //================

    [Header("チュートリアル連携")]
    public HintTutorialLinesEvent OnHintTutorialLinesRequested = new HintTutorialLinesEvent();

    [Header("State別つぶやきイベント（任意）")]
    public HintTutorialLinesEvent OnState3FullyRevealed = new HintTutorialLinesEvent();
    public HintTutorialLinesEvent OnState4FullyRevealed = new HintTutorialLinesEvent();

    [Header("行開示トリガ")]
    public UnityEvent<string> OnLineFullyRevealed = new UnityEvent<string>();

    //================
    // 行開示の履歴管理
    //================

    // 行単位で「一度でも開いた」管理（ステージ＋state＋行index）
    private readonly HashSet<string> _lineRevealedIds = new HashSet<string>();

    public bool HasLineBeenRevealed(int stage, int state, int element)
        => _lineRevealedIds.Contains(MakeId(stage, state, element));

    private static string MakeId(int stage, int state, int element)
        => $"stage{stage}.state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";

    // state単位「必要行が全部解放済みか」管理（専用セリフを一度だけ出すため）
    private readonly HashSet<string> _stateFullyRevealedIds = new HashSet<string>();
    private static string MakeStateFullId(int stage, int hintState) => $"stage{stage}.state{hintState}";

    //================
    // 開示ルール
    //================

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

    //================
    // レイアウト（リング）
    //================

    [Header("レイアウト（リング）")]
    public float RingRadius = 1.8f;
    public float OrbitSpeed = 20f;
    public float BobAmplitude = 0.15f;
    public float BobSpeed = 2.0f;
    public float HeightOffset = 1.6f;

    //================
    // 画面内チェック
    //================

    [Header("画面内チェック")]
    public bool OnlyWhenGhostOnScreen = true;
    public float OnScreenMargin = 0.05f;
    public bool CheckOcclusion = false;
    public LayerMask Occluders;
    public float CameraEyeHeight = 0.0f;

    //================
    // クローゼット中の特別表示
    //================

    [Header("クローゼット中の特別表示")]
    public bool ForceVisibleWhileHiding = true;                  // クローゼット中は必ず表示（ただし全文開示はしない）

    //================
    // 内部状態
    //================

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

    //================
    // 2部屋目の交互表示管理
    //================

    // 2部屋目で最後に選んだヒント state（3 or 4）
    // 初回は 3 を出したいので、前回 4 扱いにしておく
    private int _lastSecondRoomHintState = 4;

    // 現在表示中の 2部屋目ヒント state を固定保持する
    // 毎フレーム交互に切り替わらないようにするためのロック
    private int _currentLockedSecondRoomHintState = -1;

    //================
    // Unity Lifecycle
    //================

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
    }

    void Update()
    {
        //================
        // ゴースト追尾（差し替え時は状態を初期化）
        //================

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

                    // ゴーストが切り替わったら、次に出す 2部屋目ヒントの選択をやり直せるようにする
                    UnlockSecondRoomHintSelection();

                    ResetRevealProgress();
                    ApplyMaskedAll();
                    SelectLinesByStageAndState();
                }
            }
        }

        //================
        // 参照未設定のときは非表示
        //================

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

        //================
        // 可視判定（クローゼット中は常時表示する設定がある）
        //================

        float dist = Vector3.Distance(Player.position, Ghost.position);
        bool visibleByDistance = dist <= VisibleDistance;
        bool onScreen = !OnlyWhenGhostOnScreen || IsGhostOnScreen();
        bool isHiding = (HideRef && HideRef.hide);

        bool visible = visibleByDistance && onScreen;
        bool show = visible || (isHiding && ForceVisibleWhileHiding);

        //================
        // 初見イベント（クローゼット中は発火しない仕様）
        //================

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

        //================
        // ステージ進行＆文言選択
        //================

        CheckAndMaybeAdvanceProgress();
        SelectLinesByStageAndState();

        //================
        // 発見状態の“表示用”扱い（クローゼット中は発見扱いにしない）
        //================

        bool actuallyFound = (ChaseRef && ChaseRef.isDiscovery);  // ゲームロジック上の発見
        bool treatAsFoundForDisplay = EnableFoundOverride && actuallyFound && !isHiding;

        // 見つかった上書き（ただしクローゼット中は抑止）
        if (treatAsFoundForDisplay)
        {
            if (!_foundOverrideActive) ApplyFoundOverrideInstant();
        }
        else
        {
            if (_foundOverrideActive) ClearFoundOverride();
        }

        // 色の適用（クローゼット中は“未発見色”）
        if (treatAsFoundForDisplay != _foundPrev)
            ApplyTextColorsProfile(treatAsFoundForDisplay);
        _foundPrev = treatAsFoundForDisplay;

        //================
        // 表示切替
        //================

        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(show);
        if (!show) return;

        //================
        // レイアウト（リング状に回す）
        //================

        AnimateRingLayout();

        //================
        // 発見上書き中はそのテキストを出す
        //================

        if (_foundOverrideActive)
        {
            for (int i = 0; i < 5; i++)
                if (HintLabels[i]) HintLabels[i].text = activeLines[i];
            return;
        }

        //================
        // 通常の文字開示（クローゼット中でも“全文開示”にはしない）
        //================

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
                    // 行単位イベント（stage + hintState + element で一意）
                    int ghostStateNow = (ChaseRef ? ChaseRef.GetState() : 1);
                    int hintStateNow = ConvertGhostStateToHintState(ghostStateNow);
                    int stageIndex = Mathf.Clamp(ProgressStage, 0, Mathf.Max(0, (Stages?.Count ?? 1) - 1));

                    string id = MakeId(stageIndex, hintStateNow, currentIndex);
                    if (_lineRevealedIds.Add(id))
                    {
                        Debug.Log($"[HintText] OnLineFullyRevealed -> {id}");
                        OnLineFullyRevealed?.Invoke(id);

                        // 2部屋目ヒントミッションの進捗チェック
                        CheckSecondRoomHintsMission();

                        // state3 / state4 の「全行コンプ」チェック
                        if (hintStateNow == 3 || hintStateNow == 4)
                            TrySendStateSpecificAllLinesRevealed(hintStateNow, stageIndex);
                    }

                    currentIndex = Mathf.Min(currentIndex + 1, 4);
                    revealProgressChars = 0f;
                    waitingCooldown = true;
                    cooldownTimer = Mathf.Max(0f, NextHintCooldown);
                }
            }
        }

        //================
        // 行の最終反映
        //================

        for (int i = 0; i < 5; i++)
        {
            if (!HintLabels[i]) continue;

            if (i < currentIndex)
            {
                HintLabels[i].text = activeLines[i];              // 完全開示済み
            }
            else if (i == currentIndex && !waitingCooldown)
            {
                // 進行中は UpdateMaskedLine が既にテキスト反映
            }
            else
            {
                HintLabels[i].text = MaskAll(activeLines[i]);     // 未着手 or クールタイム中
            }
        }
    }

    //================
    // ゴースト検索
    //================

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

    //================
    // 画面内チェック
    //================

    /*
         ゴーストが画面内にいるか
         ・Viewport範囲 + margin
         ・必要なら遮蔽物Linecastで見えない扱いにする
    */
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

    //================
    // state変換
    //================

    /*
         ghostState(1/2) → hintState(1〜4)
         ・1部屋目: 1→1, 2→2
         ・2部屋目:
           - 片方が埋まっていたら未完了側を出す
           - 両方未完了なら State3 / State4 を交互に出す
    */
    private int ConvertGhostStateToHintState(int ghostState)
    {
        bool inSecondRoom =
            (SecondRoomRef != null && SecondRoomRef.IsPlayerInSecondRoom);

        if (!inSecondRoom) return ghostState;

        return GetOrLockSecondRoomHintState();
    }

    //================
    // 2部屋目の表示ルール
    //================

    /*
         現在表示する 2部屋目ヒント state（3 or 4）を返す
         ・一度決めたらロックして、毎フレームは変えない
         ・次の表示に切り替えたい時だけ UnlockSecondRoomHintSelection で解除する
    */
    private int GetOrLockSecondRoomHintState()
    {
        if (_currentLockedSecondRoomHintState == 3 || _currentLockedSecondRoomHintState == 4)
            return _currentLockedSecondRoomHintState;

        _currentLockedSecondRoomHintState = DecideNextSecondRoomHintState();
        _lastSecondRoomHintState = _currentLockedSecondRoomHintState;

        Debug.Log($"[HintText] 2部屋目ヒント選択 locked -> State{_currentLockedSecondRoomHintState}");
        return _currentLockedSecondRoomHintState;
    }

    // 2部屋目の表示固定を解除して、次回再選択できるようにする
    private void UnlockSecondRoomHintSelection()
    {
        _currentLockedSecondRoomHintState = -1;
    }

    /*
         次に出す 2部屋目ヒント state を決める
         ・片方が全部開示済みなら、未完了側を返す
         ・両方未完了なら、最後に出したものと逆を返して交互にする
         ・両方完了済みなら保険で 3 を返す
    */
    private int DecideNextSecondRoomHintState()
    {
        if (Stages == null || Stages.Count == 0) return 3;

        int stageIndex = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);

        bool state3Done = IsSecondRoomStateFullyRevealed(stageIndex, 3);
        bool state4Done = IsSecondRoomStateFullyRevealed(stageIndex, 4);

        // State3 が埋まっていて State4 が未完了なら、State4 を出す
        if (state3Done && !state4Done)
            return 4;

        // State4 が埋まっていて State3 が未完了なら、State3 を出す
        if (state4Done && !state3Done)
            return 3;

        // 両方埋まっていたら保険で 3
        if (state3Done && state4Done)
            return 3;

        // 両方未完了なら交互
        return (_lastSecondRoomHintState == 3) ? 4 : 3;
    }

    /*
         2部屋目の State3 / State4 が、そのステージで全部開示済みか
         ・空でない行だけを対象にチェックする
    */
    private bool IsSecondRoomStateFullyRevealed(int stageIndex, int hintState)
    {
        if (hintState != 3 && hintState != 4) return false;
        if (Stages == null || Stages.Count == 0) return false;

        stageIndex = Mathf.Clamp(stageIndex, 0, Stages.Count - 1);
        var set = Stages[stageIndex];
        if (set == null) return false;

        string[] src = (hintState == 3) ? set.State3 : set.State4;
        if (src == null) return true;

        for (int i = 0; i < 5; i++)
        {
            string line = (i < src.Length) ? src[i] : null;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (!HasLineBeenRevealed(stageIndex, hintState, i))
                return false;
        }

        return true;
    }

    //================
    // 文言＆色の選択
    //================

    /*
         ステージ＆状態で文言 + 色を選ぶ
         ・foundOverride中は activeLines を触らない（上書き優先）
         ・同じステージ/同じ状態/同じ内容なら更新しない
    */
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
                source = set.State1;                              // 想定外は State1 にフォールバック
                stateColors = set.State1Colors;
                break;
        }

        // 発見上書き中は activeLines を触らない
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

    //================
    // 行比較
    //================

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

    //================
    // 進行（自動）
    //================

    /*
         5行すべて開示したら、一定時間後にステージ進行する（任意）
         ・foundOverride中は進行しない
    */
    private void CheckAndMaybeAdvanceProgress()
    {
        if (_foundOverrideActive) return;

        bool allRevealed = AllFiveRevealed();

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

        // ステージが変わったら、2部屋目ヒントの固定選択を解除
        UnlockSecondRoomHintSelection();

        ResetRevealProgress();
        SelectLinesByStageAndState();
        OnProgressChanged?.Invoke(ProgressStage);
    }

    //================
    // 表示ユーティリティ（マスク）
    //================

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
        int count = Mathf.Clamp(Mathf.FloorToInt(revealedChars), 0, src.Length);
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
        return Mathf.FloorToInt(revealedChars) >= (s?.Length ?? 0);
    }

    private bool AllFiveRevealed()
    {
        if (currentIndex < 4) return false;
        return IsFullyRevealed(activeLines[4], revealProgressChars) || string.IsNullOrEmpty(activeLines[4]);
    }

    /*
         state3 / state4 について
         ・そのステージの「空でない行」が全て解放済みかチェック
         ・揃ったら一度だけ専用セリフイベントを投げる
    */
    private void TrySendStateSpecificAllLinesRevealed(int hintState, int stageIndex)
    {
        if (hintState != 3 && hintState != 4) return;
        if (Stages == null || Stages.Count == 0) return;

        stageIndex = Mathf.Clamp(stageIndex, 0, Stages.Count - 1);
        var set = Stages[stageIndex];
        if (set == null) return;

        string stateKey = MakeStateFullId(stageIndex, hintState);
        if (_stateFullyRevealedIds.Contains(stateKey)) return;

        string[] src = (hintState == 3) ? set.State3 : set.State4;
        if (src == null) return;

        for (int i = 0; i < 5; i++)
        {
            string line = (i < src.Length) ? src[i] : null;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (!HasLineBeenRevealed(stageIndex, hintState, i)) return;
        }

        _stateFullyRevealedIds.Add(stateKey);

        string[] lines = (hintState == 3) ? set.State3FullyRevealedLines : set.State4FullyRevealedLines;
        if (lines == null || lines.Length == 0) return;

        bool hasContent = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                hasContent = true;
                break;
            }
        }
        if (!hasContent) return;

        OnHintTutorialLinesRequested?.Invoke((string[])lines.Clone());

        if (hintState == 3)
        {
            Debug.Log("[HintText] State3 all hints revealed (across spawns) → OnState3FullyRevealed");
            OnState3FullyRevealed?.Invoke((string[])lines.Clone());
        }
        else
        {
            Debug.Log("[HintText] State4 all hints revealed (across spawns) → OnState4FullyRevealed");
            OnState4FullyRevealed?.Invoke((string[])lines.Clone());
        }
    }

    //================
    // 2部屋目ヒント進捗
    //================

    /*
         2部屋目用ヒント（state3/state4）
         ・「テキストが入っている行数」を need
         ・そのうち「一度でも開示した行数」を have
    */
    public void GetSecondRoomHintProgress(out int have, out int need)
    {
        have = 0;
        need = 0;

        if (Stages == null || Stages.Count == 0) return;

        int stageIndex = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stageIndex];
        if (set == null) return;

        for (int i = 0; i < 5; i++)
        {
            string s3 = (set.State3 != null && i < set.State3.Length) ? set.State3[i] : null;
            string s4 = (set.State4 != null && i < set.State4.Length) ? set.State4[i] : null;

            if (!string.IsNullOrWhiteSpace(s3)) need++;
            if (!string.IsNullOrWhiteSpace(s4)) need++;

            if (!string.IsNullOrWhiteSpace(s3) && HasLineBeenRevealed(stageIndex, 3, i)) have++;
            if (!string.IsNullOrWhiteSpace(s4) && HasLineBeenRevealed(stageIndex, 4, i)) have++;
        }
    }

    /*
         2部屋目ヒントミッションの進捗を更新する
         ・進捗イベントを飛ばす
         ・完了したら一度だけクリアイベントを飛ばし、SecondRoomTutorialへ通知する
    */
    private void CheckSecondRoomHintsMission()
    {
        if (SecondRoomRef == null || !SecondRoomRef.IsPlayerInSecondRoom) return;

        int have, need;
        GetSecondRoomHintProgress(out have, out need);

        Debug.Log($"[HintText] 2部屋目ヒント進捗 {have}/{need}");
        OnSecondRoomHintProgressUpdated?.Invoke(have, need);

        if (!_secondRoomMissionCleared && need > 0 && have >= need)
        {
            _secondRoomMissionCleared = true;
            Debug.Log("[HintText] 2部屋目ヒントミッション 完了 → OnSecondRoomHintsFullyRevealed 発火");
            OnSecondRoomHintsFullyRevealed?.Invoke();

            if (SecondRoomRef != null)
                SecondRoomRef.OnSecondRoomAllHintsRevealed();
        }
    }

    //================
    // リング配置
    //================

    // リング状に配置して、画面/ワールドどちらかに反映する
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

    //================
    // Debug
    //================

    private void OnDrawGizmosSelected()
    {
        if (!Ghost) return;

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(Ghost.position, VisibleDistance);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(Ghost.position, RevealDistance);
    }

    //================
    // 発見上書き制御
    //================

    /*
         発見時の上書き表示
         ・activeLines を FoundOverrideText で5行統一する
         ・即全開示する設定なら、開示状態も最終行まで進める
    */
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

    // 上書き解除して通常表示へ戻す
    private void ClearFoundOverride()
    {
        _foundOverrideActive = false;
        SelectLinesByStageAndState();
        ResetRevealProgress();
        ApplyMaskedAll();
    }

    // 開示進行を初期化
    private void ResetRevealProgress()
    {
        currentIndex = 0;
        revealProgressChars = 0f;
        waitingCooldown = false;
        cooldownTimer = 0f;
        _autoAdvanceTimer = -1f;
    }

    //================
    // 色適用
    //================

    // 発見中/通常のどちらの色プロファイルを使うか反映する
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

    // 行ごとの色配列を適用する
    private void ApplyLineColors(Color[] colors)
    {
        EnsureColorArraySize(ref colors, 5, Color.white);
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].color = colors[Mathf.Clamp(i, 0, colors.Length - 1)];
    }

    // Color配列が未設定/サイズ違いなら、足りない分をfillで埋めた配列に差し替える
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