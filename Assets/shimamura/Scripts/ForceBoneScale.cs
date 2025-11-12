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
    [SerializeField] private Animator animatorOverride; // 子/親にある場合のため

    // ── Y制御 ───────────────────────────────────────────
    [Header("Yをアニメから使う条件")]
    public string allowYStateTag = "Climb";  // ステートTag名
    public string allowYBoolParam = "IsClimbing"; // Bool名（空でも可）
    public int layerIndex = 0;

    [Header("挙動オプション")]
    public bool clampYToOnlyGoUp = true;
    public bool applyDeltaRotation = true;
    public bool forceApplyRootMotion = true;

    // ── Genericサポート ────────────────────────────────
    [Header("Genericモード（Humanoidでない場合はこちらを使用）")]
    public bool genericMode = false;
    public Transform leftFoot;   // Generic用
    public Transform rightFoot;  // Generic用

    // ── “足高さ＝地面” 固定 ────────────────────────────
    [Header("Feet-as-Ground")]
    [Tooltip("起動時の足高さを地面として固定します")]
    public bool useFootHeightAsGroundOnStart = true;
    [Tooltip("左右の足のどちらを基準にするか（Min：低い方、Max：高い方、Average：平均）")]
    public GroundMode groundMode = GroundMode.Min;
    public enum GroundMode { Min, Max, Average }

    [Tooltip("地面とみなす足高さに足底オフセットを加算（足裏厚みなど）")]
    public float footBaseOffset = 0f;

    [Tooltip("当たり抜け防止の微小余白。最終的な基準高さに加算されます")]
    public float safetyMargin = 0.0f;

    // ── デバッグ ───────────────────────────────────────
    [Header("Debug Logs")]
    public bool verboseLog = true;
    public bool logEveryFrame = false;
    public bool logOnlyWhenClamped = true;
    public bool logOnValidate = true;
    public bool drawGizmos = true;

    // ===== 追加: 外部から“登り中扱い”を強制する保険（任意） =====
    [Header("Manual Override (任意)")]
    [SerializeField] private bool forceClimbOverride = false;
    public void SetClimbOverride(bool on)
    {
        forceClimbOverride = on;
        if (verboseLog) Debug.Log($"[FBS] SetClimbOverride => {on}");
    }

    // ===== 追加: 登り終了で自動Rebase & 1～2フレーム猶予 =====
    [Header("Climb End Handling")]
    [Tooltip("登り終了時に足基準を現在の高さへ取り直す")]
    public bool rebaseOnClimbEnd = true;
    [Tooltip("Rebase直後にロック処理をスキップするフレーム数")]
    public int bypassFramesAfterRebase = 2;

    // ── 内部状態 ───────────────────────────────────────
    Dictionary<Transform, Vector3> _initialScales;
    Animator _anim;
    int _allowYBoolHash;
    bool _allowYNow, _allowYPrev;

    // Humanoid足参照
    Transform _lfHum, _rfHum;

    // “足＝地面”のベースY
    float _feetGroundY;
    bool _hasGroundBase;

    // 基準Y（Y固定用）
    float _frozenY;

    // 追加: バイパス用カウンタ
    int _bypassCounter = 0;

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
        // 子→自分→親の順で確実にAnimator取得
        _anim = animatorOverride
                ? animatorOverride
                : (GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>() ?? GetComponentInParent<Animator>());

        if (root == null) root = _anim ? _anim.transform : transform;

        _initialScales = new Dictionary<Transform, Vector3>(128);
        if (includeChildren)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                _initialScales[t] = t.localScale;
        }
        else
        {
            _initialScales[root] = root.localScale;
        }

        if (!string.IsNullOrEmpty(allowYBoolParam))
            _allowYBoolHash = Animator.StringToHash(allowYBoolParam);

        if (_anim && forceApplyRootMotion) _anim.applyRootMotion = true;

        if (_anim && _anim.isHuman)
        {
            _lfHum = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rfHum = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        _frozenY = transform.position.y;

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
        // Y許可判定（＝登り中フラグ）
        bool byTag = false;
        if (_anim && !string.IsNullOrEmpty(allowYStateTag))
        {
            var st = _anim.GetCurrentAnimatorStateInfo(layerIndex);
            var nst = _anim.GetNextAnimatorStateInfo(layerIndex);
            byTag = st.IsTag(allowYStateTag) || (_anim.IsInTransition(layerIndex) && nst.IsTag(allowYStateTag));
        }
        bool byBool = (!string.IsNullOrEmpty(allowYBoolParam) && _anim) ? _anim.GetBool(_allowYBoolHash) : false;

        _allowYPrev = _allowYNow;
        _allowYNow = forceClimbOverride || byTag || byBool;

        // ▼ 追加：下降エッジで自動Rebase＋バイパス付与
        if (_allowYPrev && !_allowYNow)
        {
            if (rebaseOnClimbEnd)
            {
                RebaseFeetGround("ClimbEnd");
            }
            _bypassCounter = Mathf.Max(0, bypassFramesAfterRebase);
            if (verboseLog) Debug.Log($"[FBS] Climb END → Rebase & bypass {_bypassCounter}f");
        }

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
        if (_anim == null) return;

        Vector3 before = transform.position;

        // RootMotion位置（登り中のみYも通す）
        Vector3 d = _anim.deltaPosition;
        if (!_allowYNow) d.y = 0f;

        Vector3 next = before + d;

        // “足＝地面”より下へは行かせない（RootMotion段階の簡易クランプ）
        float pushRM = 0f;
        if (_hasGroundBase)
            pushRM = RequiredPushUpToKeepFeetAboveGround(next.y);
        if (pushRM > 0f) next.y += pushRM;

        // 許可中でも“下方向”は無視（任意）
        if (_allowYNow && clampYToOnlyGoUp && next.y < _frozenY)
            next.y = _frozenY;

        transform.position = next;

        if (applyDeltaRotation)
            transform.rotation *= _anim.deltaRotation;

        if (_allowYNow)
            _frozenY = Mathf.Max(_frozenY, transform.position.y);

        LogFrame("OnAnimatorMove", before, next, d, pushRM, note: pushRM > 0f ? "RMClamp" : null);
    }

    void LateUpdate()
    {
        Vector3 before = transform.position;

        // スケール固定（常時）
        foreach (var kv in _initialScales) kv.Key.localScale = kv.Value;

        // 登り中はロック解除（補正スキップ）
        if (_allowYNow)
        {
            LogFrame("LateUpdate", before, transform.position, Vector3.zero, 0f, note: "BypassWhileClimb");
            return;
        }

        // Rebase直後の数フレームはスキップ（戻り抑止）
        if (_bypassCounter > 0)
        {
            _bypassCounter--;
            if (verboseLog) Debug.Log($"[FBS] Bypass after rebase... {_bypassCounter}f left");
            return;
        }

        // 既存の“常に一致（上下補正）”
        float pushLate = 0f;
        if (_hasGroundBase && TryGetFootYs(out float yL, out float yR, out _))
        {
            float feetY = PickFeetY(yL, yR); // 現在の足底高さ
            float want = _feetGroundY;      // 目標（Rebase＋オフセット＋マージン）
            float diff = want - feetY;      // 上下どちらも補正

            if (Mathf.Abs(diff) > 1e-6f)
            {
                var p = transform.position;
                p.y += diff;                 // 浮いていれば下げる／沈んでいれば上げる
                transform.position = p;
                pushLate = diff;
            }

            if (diff > 0f) _frozenY = Mathf.Max(_frozenY, transform.position.y);
        }
        else
        {
            var p = transform.position;
            p.y = Mathf.Max(p.y, _frozenY);
            transform.position = p;
        }

        LogFrame("LateUpdate", before, transform.position, Vector3.zero, pushLate, note: Mathf.Abs(pushLate) > 0f ? "HardLock" : null);
    }

    // —— 公開：いつでも“足＝地面”を取り直せる ——
    [ContextMenu("Rebase Feet Ground")]
    public void RebaseFeetGroundContext() => RebaseFeetGround("ContextMenu");

    public void RebaseFeetGround(string reason = "")
    {
        var lf = GetLeftFoot();
        var rf = GetRightFoot();

        if (lf == null && rf == null)
        {
            _hasGroundBase = false;
            if (verboseLog) Debug.LogWarning($"[FBS] 足Transformが取れないため、Feet-as-Groundは無効です ({reason})");
            return;
        }

        float yL = lf ? lf.position.y : float.NaN;
        float yR = rf ? rf.position.y : float.NaN;

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
            baseY = lf ? yL : yR; // どちらか片方
        }

        _feetGroundY = baseY + footBaseOffset + safetyMargin;
        _hasGroundBase = true;

        // いまの足高さに即一致させる（上げでも下げでも）
        if (TryGetFootYs(out float yL2, out float yR2, out _))
        {
            float feetY = PickFeetY(yL2, yR2);
            float diff = _feetGroundY - feetY;
            if (Mathf.Abs(diff) > 1e-6f)
            {
                var p = transform.position; p.y += diff; transform.position = p;
                if (diff > 0f) _frozenY = Mathf.Max(_frozenY, p.y);
            }
        }

        if (verboseLog)
        {
            Debug.Log($"[FBS] Rebased Feet-Ground ({reason}) feetGroundY={_feetGroundY:F4}");
            DumpFootRefs("Rebase");
        }
    }

    // 足が“地面”より下に行かないために必要な押し上げ量（RM用）
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

    // —— 足高さの取得（Humanoid/Generic） ——
    bool TryGetFootYs(out float yL, out float yR, out string src)
    {
        // 明示指定があれば最優先
        if (IsValidFootPair(leftFoot, rightFoot))
        {
            yL = leftFoot.position.y;
            yR = rightFoot.position.y;
            src = "assignedBones";
            return true;
        }

        // Humanoidならボーン名で取る
        if (_anim && _anim.isHuman)
        {
            var lf = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
            if (IsValidFootPair(lf, rf))
            {
                yL = lf.position.y;
                yR = rf.position.y;
                src = "humanoidBones";
                return true;
            }
        }

        // 取れない
        yL = yR = 0f;
        src = "none";
        return false;
    }

    static bool IsValidFootPair(Transform l, Transform r)
    {
        if (l == null || r == null) return false;
        if (l == r) return false; // 左右同一は無効
        return true;
    }

    float SafeY(Transform t) => t ? t.position.y : float.NaN;

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

    float PickFeetY(float yL, float yR)
    {
        switch (groundMode)
        {
            case GroundMode.Min: return Mathf.Min(yL, yR);
            case GroundMode.Max: return Mathf.Max(yL, yR);
            default /*Average*/: return 0.5f * (yL + yR);
        }
    }

    void DumpFootRefs(string where)
    {
        var lf = GetLeftFoot();
        var rf = GetRightFoot();
        string lfLabel = lf ? lf.name : "null";
        string rfLabel = rf ? rf.name : "null";
        Debug.Log($"[FBS][{where}][Refs] {name} LF={lfLabel} y={SafeY(lf):F4}  RF({rfLabel}) y={SafeY(rf):F4}  feetGroundY={_feetGroundY:F4} hasBase={_hasGroundBase} frozenY={_frozenY:F4}");
    }

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
            $"LF({lfName})={lfY:F4} RF({rfName})={rfY:F4} generic={genericMode} human={(_anim && _anim.isHuman)}"
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
