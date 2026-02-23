// ForceBoneScale.cs
//
// 目的
// ボーンの localScale がアニメや他処理で崩れるのを防ぐため、初期値を保存して毎フレーム元に戻す
// 通常はY移動を固定して、沈みや浮きなど見た目の崩れを抑える
// 登りなど特定条件のときだけ、Animator の rootMotion によるY移動を許可する
// 足の高さを基準にして、足が地面基準より下に潜る場合に押し上げ補正する
// 外部スナップ直後に下方向へ戻される挙動を、数フレームだけ無効化する（ハードロック）
//
// 注意
// PushController 側がプロパティ名や公開フィールド名に依存しているため、名前は変更しない

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ForceBoneScale : MonoBehaviour
{
    [Tooltip("trueなら子ボーンも全て固定（通常はtrue推奨）")]
    public bool includeChildren = true;

    [Tooltip("Animatorのあるルートより下だけを固定します")]
    public Transform root; // 空なら Animator の Transform

    [Header("Animator Ref (任意)")]
    [SerializeField] private Animator animatorOverride;

    // Y制御
    // allowYStateTag または allowYBoolParam が成立した時だけ、rootMotionのY移動を許可する
    // それ以外は rootMotion のY成分を捨てて、現在の高さを維持する
    [Header("Yをアニメから使う条件")]
    public string allowYStateTag = "Climb";
    public string allowYBoolParam = "IsClimbing";
    public int layerIndex = 0;

    // 挙動オプション
    // clampYToOnlyGoUp は、登り中に下方向へ戻されるのを禁止して「登った高さ」を維持する
    // applyDeltaRotation は、rootMotionの回転を適用するかどうか
    // forceApplyRootMotion は、Animator.applyRootMotion を強制的に true にする（必要時のみ）
    [Header("挙動オプション")]
    public bool clampYToOnlyGoUp = true;
    public bool applyDeltaRotation = true;
    public bool forceApplyRootMotion = false;

    // Genericサポート
    // Humanoid でないリグの場合、HumanBodyBones から足を取れないため、足Transformを手動で割り当てる
    [Header("Genericモード（Humanoidでない場合はこちら）")]
    public bool genericMode = false;
    public Transform leftFoot;
    public Transform rightFoot;

    // Feet-as-Ground
    // 起動時や任意タイミングで「足の高さ」を地面基準として記録し、
    // 以後、足がその基準より下に潜ったら差分だけ押し上げる
    [Header("Feet-as-Ground")]
    public bool useFootHeightAsGroundOnStart = true;
    public GroundMode groundMode = GroundMode.Min;
    public enum GroundMode { Min, Max, Average }
    public float footBaseOffset = 0f;
    public float safetyMargin = 0.0f;

    // Debug
    // verboseLog は状態変化ログなどを出す
    // logEveryFrame は毎フレームログ（重いので通常false）
    // logOnlyWhenClamped は補正が発生した時だけログを出す
    [Header("Debug Logs")]
    public bool verboseLog = true;
    public bool logEveryFrame = false;
    public bool logOnlyWhenClamped = true;
    public bool logOnValidate = true;
    public bool drawGizmos = true;

    // Manual override
    // 外部から登り扱いに固定したい時に使う（PushControllerから呼ばれる想定）
    [Header("Manual Override (任意)")]
    [SerializeField] private bool forceClimbOverride = false;
    public void SetClimbOverride(bool on)
    {
        forceClimbOverride = on;
        if (verboseLog) Debug.Log($"[FBS] SetClimbOverride => {on}");
    }

    // 登り終了時の処理
    // rebaseOnClimbEnd は登り終了時に足基準を取り直す
    // bypassFramesAfterRebase は取り直し直後の数フレームだけ補正を抑制する（揺れ防止用）
    [Header("Climb End Handling")]
    public bool rebaseOnClimbEnd = false;
    public int bypassFramesAfterRebase = 0;

    // 外部スナップ後のロック
    // 位置を外部で補正した直後に、次フレームで下方向へ戻されると見た目が悪いので
    // 一定フレームだけ「下方向成分」を無視する
    [Header("Lock After External Snap")]
    [Tooltip("SetGroundYAndSnap() 直後、下方向の移動を無視するフレーム数")]
    public int lockFramesAfterExternalSnap = 2;

    // 内部状態
    // _initialScales は初期localScaleの保存先（LateUpdateで毎フレーム戻す）
    Dictionary<Transform, Vector3> _initialScales;
    Animator _anim;
    int _allowYBoolHash;
    bool _allowYNow, _allowYPrev;

    // Humanoid時の足参照（Humanoidの場合のみここが使われる）
    Transform _lfHum, _rfHum;

    // 足基準（地面）Y と有効フラグ
    float _feetGroundY;
    bool _hasGroundBase;

    // 通常時の最低Y（これより下に落ちないための基準）
    float _frozenY;

    // Rebase直後の補正抑制用カウンタ
    int _bypassCounter = 0;

    // 外部スナップ直後の下方向ロックカウンタ
    int _hardLockCounter = 0;

    // 公開読み取り用（PushControllerから参照）
    // ここは参照元があるので名前を変えない
    public bool IsYFree => _allowYNow;
    public bool HasGroundBase => _hasGroundBase;
    public float FeetGroundY => _feetGroundY;
    public int HardLockFramesLeft => _hardLockCounter;

    void OnValidate()
    {
#if UNITY_EDITOR
        if (!logOnValidate) return;

        Debug.Log($"[FBS][Validate] {name} generic={genericMode}, allowTag='{allowYStateTag}', allowBool='{allowYBoolParam}', layer={layerIndex}");

        if (genericMode)
        {
            if (leftFoot == null || rightFoot == null)
                Debug.LogWarning($"[FBS][Validate] {name} Genericモードですがleft/rightFootが未割当です。");
        }
#endif
    }

    void Awake()
    {
        // Animatorの取得
        // overrideがあればそれを優先し、無ければ自分/子/親を探索する
        _anim = animatorOverride
                ? animatorOverride
                : (GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>() ?? GetComponentInParent<Animator>());

        // scale固定の対象ルート
        // root未指定なら Animator の transform を基準にする（Animator階層以下を固定したい想定）
        if (root == null) root = _anim ? _anim.transform : transform;

        // 初期localScaleの保存
        // LateUpdateで毎フレームこの値に戻すことで、スケール崩れを抑える
        _initialScales = new Dictionary<Transform, Vector3>(128);
        if (includeChildren)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t) _initialScales[t] = t.localScale;
        }
        else
        {
            if (root) _initialScales[root] = root.localScale;
        }

        // Boolパラメータはハッシュ化して参照コストを下げる
        if (!string.IsNullOrEmpty(allowYBoolParam))
            _allowYBoolHash = Animator.StringToHash(allowYBoolParam);

        // 必要なら rootMotion を強制的に有効化する
        if (_anim && forceApplyRootMotion) _anim.applyRootMotion = true;

        // Humanoidなら足ボーンを確保しておく（足Y計測に使う）
        if (_anim && _anim.isHuman)
        {
            _lfHum = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rfHum = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        // 通常時の最低Yを現在位置で初期化
        _frozenY = transform.position.y;

        // 起動時に足基準を作っておく（足沈み補正をすぐ効かせたい場合）
        if (useFootHeightAsGroundOnStart)
            RebaseFeetGround("Awake");

        if (verboseLog)
        {
            var animName = _anim ? _anim.name : "null";
            Debug.Log($"[FBS] Awake: Animator='{animName}' initY={_frozenY:F3} applyRootMotion={(_anim ? _anim.applyRootMotion : false)}, isHuman={(_anim && _anim.isHuman)} generic={genericMode}");
            DumpFootRefs("Awake");
        }
    }

    void Update()
    {
        // ここでは「Y移動を許可する状態か」を毎フレーム判定する
        // forceClimbOverride が true なら常に許可
        // そうでなければ Tag または Bool が成立している時だけ許可
        bool byTag = false;
        bool byBool = false;

        if (_anim)
        {
            if (LayerValid() && !string.IsNullOrEmpty(allowYStateTag))
            {
                var st = _anim.GetCurrentAnimatorStateInfo(layerIndex);
                var nst = _anim.GetNextAnimatorStateInfo(layerIndex);

                // 遷移中も含めてタグ一致を拾う（登りへの遷移開始/終了で取りこぼさないため）
                byTag = st.IsTag(allowYStateTag) || (_anim.IsInTransition(layerIndex) && nst.IsTag(allowYStateTag));
            }

            if (!string.IsNullOrEmpty(allowYBoolParam))
                byBool = _anim.GetBool(_allowYBoolHash);
        }

        _allowYPrev = _allowYNow;
        _allowYNow = forceClimbOverride || byTag || byBool;

        // 登り状態が終わった瞬間の処理
        // 足基準を取り直すかどうか、補正を数フレーム抑制するかどうかをここで決める
        if (_allowYPrev && !_allowYNow)
        {
            if (rebaseOnClimbEnd) RebaseFeetGround("ClimbEnd");
            _bypassCounter = Mathf.Max(0, bypassFramesAfterRebase);

            if (verboseLog) Debug.Log($"[FBS] Climb END → Rebase & bypass {_bypassCounter}f");
        }

        // 状態変化ログ（原因が分かるように理由を出す）
        if (verboseLog && _allowYPrev != _allowYNow)
        {
            string reason = forceClimbOverride ? "forceOverride"
                             : byBool ? $"Bool:{allowYBoolParam}=true"
                             : byTag ? $"Tag:{allowYStateTag}"
                             : "none";

            Debug.Log($"[FBS] ClimbFlag changed => now={_allowYNow} (reason={reason}) layer={layerIndex}");
        }
    }

    void OnAnimatorMove()
    {
        // Animatorの rootMotion がある場合、deltaPosition / deltaRotation はここで取得できる
        // ここで transform を更新しているため、Y制御や足押し上げ補正はこのタイミングで入れる
        if (_anim == null) return;

        Vector3 before = transform.position;

        // rootMotionの移動量
        Vector3 d = _anim.deltaPosition;

        // 外部スナップ直後ロック中は、下方向のrootMotion成分を捨てる
        // 位置補正直後にガクッと落ちるのを防ぐ目的
        if (_hardLockCounter > 0 && d.y < 0f) d.y = 0f;

        // 通常時はY移動を許可しないので、rootMotionのY成分を捨てる
        if (!_allowYNow) d.y = 0f;

        Vector3 next = before + d;

        // 足基準がある場合は、次位置で足が潜る分だけ押し上げる
        float pushRM = 0f;
        if (_hasGroundBase)
            pushRM = RequiredPushUpToKeepFeetAboveGround(next.y);
        if (pushRM > 0f) next.y += pushRM;

        // 登り中は、下方向へ戻す移動を禁止して最大到達Yを維持する
        if (_allowYNow && clampYToOnlyGoUp && next.y < _frozenY)
            next.y = _frozenY;

        transform.position = next;

        // rootMotion回転を適用する（必要な時だけ）
        if (applyDeltaRotation)
            transform.rotation *= _anim.deltaRotation;

        // 登り中は「最大到達Y」を更新していく（下がらないための基準）
        if (_allowYNow)
            _frozenY = Mathf.Max(_frozenY, transform.position.y);

        LogFrame("OnAnimatorMove", before, next, d, pushRM, note: pushRM > 0f ? "RMClamp" : null);
    }

    void LateUpdate()
    {
        // scale固定はLateUpdateで行う
        // ほかの処理がscaleを変更したあとに元へ戻すのが目的
        Vector3 before = transform.position;

        foreach (var kv in _initialScales)
            if (kv.Key) kv.Key.localScale = kv.Value;

        // 登り中は足押し上げ補正は行わない（登りのYはアニメに任せる）
        // ただし外部スナップ直後ロックのカウンタは進める
        if (_allowYNow)
        {
            LogFrame("LateUpdate", before, transform.position, Vector3.zero, 0f, note: "WhileClimb");
            if (_hardLockCounter > 0) _hardLockCounter--;
            return;
        }

        // Rebase直後に数フレーム補正を抑制したい場合はここで抜ける
        if (_bypassCounter > 0)
        {
            _bypassCounter--;
            if (verboseLog) Debug.Log($"[FBS] Bypass after rebase... {_bypassCounter}f left");
            if (_hardLockCounter > 0) _hardLockCounter--;
            return;
        }

        // 通常時の足沈み補正
        // 足Yが取得できれば、基準との差分だけYを動かす
        float pushLate = 0f;
        if (_hasGroundBase && TryGetFootYs(out float yL, out float yR, out _))
        {
            float feetY = PickFeetY(yL, yR);
            float want = _feetGroundY;
            float diff = want - feetY;

            // 外部スナップ直後ロック中は、下げ補正を無視する（落下防止）
            if (_hardLockCounter > 0 && diff < 0f) diff = 0f;

            if (Mathf.Abs(diff) > 1e-6f)
            {
                ApplyDeltaY(diff);
                pushLate = diff;
            }

            // 押し上げが発生した場合は最低Yも更新して沈みにくくする
            if (diff > 0f) _frozenY = Mathf.Max(_frozenY, transform.position.y);
        }
        else
        {
            // 足が取れない場合は、最低でも frozenY 以上を維持する（最後の保険）
            float diff = Mathf.Max(0f, _frozenY - transform.position.y);
            if (Mathf.Abs(diff) > 1e-6f) ApplyDeltaY(diff);
        }

        LogFrame("LateUpdate", before, transform.position, Vector3.zero, pushLate, note: Mathf.Abs(pushLate) > 0f ? "HardLock" : null);

        if (_hardLockCounter > 0) _hardLockCounter--;
    }

    // 外部から現在の足基準Yを参照したい場合に使う
    public bool TryGetFeetGroundY(out float y)
    {
        y = _feetGroundY;
        return _hasGroundBase;
    }

    // 外部から足基準Yを指定し、現在の足位置がそこに合うようにスナップする
    // 直後に下方向へ戻されないように、ロックカウンタを設定する
    public void SetGroundYAndSnap(float groundY, string reason = "External")
    {
        _feetGroundY = groundY;
        _hasGroundBase = true;

        // 現在の足Yを取得して、基準との差分だけ移動する
        if (TryGetFootYs(out float yL2, out float yR2, out _))
        {
            float feetY = PickFeetY(yL2, yR2);
            float diff = _feetGroundY - feetY;

            if (Mathf.Abs(diff) > 1e-6f)
            {
                ApplyDeltaY(diff);
                if (diff > 0f) _frozenY = Mathf.Max(_frozenY, transform.position.y);
            }
        }

        // 外部スナップ直後の下方向ロックを設定する
        _hardLockCounter = Mathf.Max(_hardLockCounter, lockFramesAfterExternalSnap);

        if (verboseLog) Debug.Log($"[FBS] SetGroundYAndSnap({reason}) groundY={_feetGroundY:F4} lock={_hardLockCounter}f");
    }

    [ContextMenu("Rebase Feet Ground")]
    public void RebaseFeetGroundContext() => RebaseFeetGround("ContextMenu");

    // 現在の足位置を足基準として取り直す
    // 起動時や、登り終了時など「ここを地面として扱いたい」タイミングで呼ぶ
    public void RebaseFeetGround(string reason = "")
    {
        var lf = GetLeftFoot();
        var rf = GetRightFoot();

        // 足が取れない場合は足基準機能を無効化する
        if (lf == null && rf == null)
        {
            _hasGroundBase = false;
            if (verboseLog) Debug.LogWarning($"[FBS] 足Transformが取れないため、Feet-as-Groundは無効です ({reason})");
            return;
        }

        float yL = lf ? lf.position.y : float.NaN;
        float yR = rf ? rf.position.y : float.NaN;

        // 左右のどの値を採用するか（Min/Max/Average）
        float baseY;
        if (lf && rf)
        {
            switch (groundMode)
            {
                case GroundMode.Min: baseY = Mathf.Min(yL, yR); break;
                case GroundMode.Max: baseY = Mathf.Max(yL, yR); break;
                default: baseY = 0.5f * (yL + yR); break;
            }
        }
        else
        {
            baseY = lf ? yL : yR;
        }

        // 記録する足基準Y（オフセットと安全マージンを足した値）
        _feetGroundY = baseY + footBaseOffset + safetyMargin;
        _hasGroundBase = true;

        // 取り直した基準に今すぐ合わせる（差分だけ移動）
        if (TryGetFootYs(out float yL2, out float yR2, out _))
        {
            float feetY = PickFeetY(yL2, yR2);
            float diff = _feetGroundY - feetY;

            if (Mathf.Abs(diff) > 1e-6f)
            {
                ApplyDeltaY(diff);
                if (diff > 0f) _frozenY = Mathf.Max(_frozenY, transform.position.y);
            }
        }

        if (verboseLog)
        {
            Debug.Log($"[FBS] Rebased Feet-Ground ({reason}) feetGroundY={_feetGroundY:F4}");
            DumpFootRefs("Rebase");
        }
    }

    // Y差分移動を適用する
    // CharacterController があれば CC.Move を使い、無ければ transform.position を直接変更する
    void ApplyDeltaY(float diff)
    {
        if (Mathf.Abs(diff) < 1e-6f) return;

        var cc = GetComponent<CharacterController>();
        if (cc && cc.enabled) cc.Move(new Vector3(0f, diff, 0f));
        else
        {
            var p = transform.position;
            p.y += diff;
            transform.position = p;
        }
    }

    // 次フレームのrootY(nextY)を仮定し、その時点で足が基準より下に潜るなら必要な押し上げ量を返す
    // rootMotionでYを動かす前に押し上げを入れるため、OnAnimatorMove側で使う
    float RequiredPushUpToKeepFeetAboveGround(float nextY)
    {
        var lf = GetLeftFoot();
        var rf = GetRightFoot();
        if (!_hasGroundBase || (lf == null && rf == null)) return 0f;

        float rootDelta = nextY - transform.position.y;

        float needL = 0f, needR = 0f;
        if (lf) needL = _feetGroundY - (lf.position.y + rootDelta);
        if (rf) needR = _feetGroundY - (rf.position.y + rootDelta);

        float need = Mathf.Max(needL, needR, 0f);

        if (logEveryFrame && (!logOnlyWhenClamped || need > 0f))
        {
            string lfName = lf ? lf.name : "null";
            string rfName = rf ? rf.name : "null";
            Debug.Log($"[FBS][PushCalc] nextY={nextY:F4} rootDelta={rootDelta:F4} feetGroundY={_feetGroundY:F4}  L({lfName})Now={SafeY(lf):F4} needL={needL:F4}  R({rfName})Now={SafeY(rf):F4} needR={needR:F4}  -> need={need:F4}");
        }

        return need > 0f ? need : 0f;
    }

    // 足Yを取得する
    // InspectorでleftFoot/rightFootが両方指定されていればそれを優先（Generic用）
    // HumanoidならHumanBodyBonesから取得した足を使う
    bool TryGetFootYs(out float yL, out float yR, out string src)
    {
        if (IsValidFootPair(leftFoot, rightFoot))
        {
            yL = leftFoot.position.y;
            yR = rightFoot.position.y;
            src = "assignedBones";
            return true;
        }

        if (_anim && _anim.isHuman)
        {
            var lf = _lfHum ? _lfHum : _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = _rfHum ? _rfHum : _anim.GetBoneTransform(HumanBodyBones.RightFoot);
            if (IsValidFootPair(lf, rf))
            {
                yL = lf.position.y;
                yR = rf.position.y;
                src = "humanoidBones";
                return true;
            }
        }

        yL = yR = 0f;
        src = "none";
        return false;
    }

    // 左右の足Transformが正しく揃っているかのチェック
    static bool IsValidFootPair(Transform l, Transform r)
    {
        if (l == null || r == null) return false;
        if (l == r) return false;
        return true;
    }

    float SafeY(Transform t) => t ? t.position.y : float.NaN;

    // 足参照の取得
    // genericMode または Humanoidでない場合は Inspector指定を使う
    // HumanoidならAwakeで確保したHumanBodyBones参照を使う
    Transform GetLeftFoot()
    {
        if (genericMode || _anim == null || !_anim.isHuman) return leftFoot;
        return _lfHum;
    }

    Transform GetRightFoot()
    {
        if (genericMode || _anim == null || !_anim.isHuman) return rightFoot;
        return _rfHum;
    }

    // 左右どちらの足Yを採用するか（Min/Max/Average）
    float PickFeetY(float yL, float yR)
    {
        switch (groundMode)
        {
            case GroundMode.Min: return Mathf.Min(yL, yR);
            case GroundMode.Max: return Mathf.Max(yL, yR);
            default: return 0.5f * (yL + yR);
        }
    }

    // layerIndex が Animator のレイヤー範囲内かを確認する
    bool LayerValid() => _anim && layerIndex >= 0 && layerIndex < _anim.layerCount;

    // 初期scale辞書を作り直す
    // 途中で対象階層が変わった場合など、必要なら外部から呼ぶ
    public void RebuildScaleTargets()
    {
        _initialScales.Clear();

        if (includeChildren)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t) _initialScales[t] = t.localScale;
        }
        else if (root)
        {
            _initialScales[root] = root.localScale;
        }
    }

    // 足参照の状態をログ出力する（デバッグ用）
    void DumpFootRefs(string where)
    {
        var lf = GetLeftFoot();
        var rf = GetRightFoot();
        string lfLabel = lf ? lf.name : "null";
        string rfLabel = rf ? rf.name : "null";
        Debug.Log($"[FBS][{where}][Refs] {name} LF={lfLabel} y={SafeY(lf):F4}  RF({rfLabel}) y={SafeY(rf):F4}  feetGroundY={_feetGroundY:F4} hasBase={_hasGroundBase} frozenY={_frozenY:F4}");
    }

    // 必要な時だけフレームログを出す
    // logEveryFrame が true なら毎フレーム
    // false の場合は logOnlyWhenClamped に従い、押し上げが発生した時だけ出す
    void LogFrame(string phase, Vector3 before, Vector3 after, Vector3 deltaRM, float push, string note = null)
    {
        if (!logEveryFrame && !(logOnlyWhenClamped && Mathf.Abs(push) > 0f)) return;

        var lf = GetLeftFoot();
        var rf = GetRightFoot();

        float lfY = SafeY(lf);
        float rfY = SafeY(rf);

        string tag = _allowYNow ? "[Y FREE]" : "[Y FIXED]";
        string suffix = string.IsNullOrEmpty(note) ? "" : $" ({note})";
        string lfName = lf ? lf.name : "null";
        string rfName = rf ? rf.name : "null";

        Debug.Log(
            $"[FBS]{suffix} {phase} f#{Time.frameCount} {tag} pos {before} -> {after} ΔRM=({deltaRM.x:F3},{deltaRM.y:F3},{deltaRM.z:F3}) push={push:+0.0000;-0.0000} " +
            $"feetGroundY={_feetGroundY:F4} frozenY={_frozenY:F4} " +
            $"LF({lfName})={lfY:F4} RF({rfName})={rfY:F4} generic={genericMode} human={(_anim && _anim.isHuman)} hardLock={_hardLockCounter}"
        );
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = Color.yellow;
        var p = transform.position;
        Gizmos.DrawLine(p + Vector3.left * 2f, p + Vector3.right * 2f);

        if (_hasGroundBase)
        {
            Gizmos.color = Color.cyan;
            Vector3 a = new Vector3(p.x - 1.5f, _feetGroundY, p.z - 1.5f);
            Vector3 b = new Vector3(p.x + 1.5f, _feetGroundY, p.z + 1.5f);
            Gizmos.DrawLine(new Vector3(a.x, _feetGroundY, a.z), new Vector3(b.x, _feetGroundY, a.z));
            Gizmos.DrawLine(new Vector3(a.x, _feetGroundY, b.z), new Vector3(b.x, _feetGroundY, b.z));
        }

        var lf = GetLeftFoot();
        var rf = GetRightFoot();
        Gizmos.color = Color.magenta;
        if (lf) Gizmos.DrawSphere(lf.position, 0.03f);
        if (rf) Gizmos.DrawSphere(rf.position, 0.03f);
    }
}