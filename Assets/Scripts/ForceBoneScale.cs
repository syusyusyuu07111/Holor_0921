// ForceBoneScale.cs (Trace用プロパティ＋ハードロック付き 完全版)
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

    // ── Y制御 ───────────────────────────────────────────
    [Header("Yをアニメから使う条件")]
    public string allowYStateTag = "Climb";
    public string allowYBoolParam = "IsClimbing";
    public int layerIndex = 0;

    [Header("挙動オプション")]
    public bool clampYToOnlyGoUp = true;
    public bool applyDeltaRotation = true;
    public bool forceApplyRootMotion = false;

    // ── Genericサポート ────────────────────────────────
    [Header("Genericモード（Humanoidでない場合はこちら）")]
    public bool genericMode = false;
    public Transform leftFoot;
    public Transform rightFoot;

    // ── Feet-as-Ground ────────────────────────────────
    [Header("Feet-as-Ground")]
    public bool useFootHeightAsGroundOnStart = true;
    public GroundMode groundMode = GroundMode.Min;
    public enum GroundMode { Min, Max, Average }
    public float footBaseOffset = 0f;
    public float safetyMargin = 0.0f;

    // ── Debug ─────────────────────────────────────────
    [Header("Debug Logs")]
    public bool verboseLog = true;
    public bool logEveryFrame = false;
    public bool logOnlyWhenClamped = true;
    public bool logOnValidate = true;
    public bool drawGizmos = true;

    // Manual override
    [Header("Manual Override (任意)")]
    [SerializeField] private bool forceClimbOverride = false;
    public void SetClimbOverride(bool on)
    {
        forceClimbOverride = on;
        if (verboseLog) Debug.Log($"[FBS] SetClimbOverride => {on}");
    }

    // 登り終了の自動処理（今回は外部主導）
    [Header("Climb End Handling")]
    public bool rebaseOnClimbEnd = false;
    public int bypassFramesAfterRebase = 0;

    [Header("Lock After External Snap")]
    [Tooltip("SetGroundYAndSnap() 直後、下方向の移動を無視するフレーム数")]
    public int lockFramesAfterExternalSnap = 2;

    // ── 内部状態 ───────────────────────────────────────
    Dictionary<Transform, Vector3> _initialScales;
    Animator _anim;
    int _allowYBoolHash;
    bool _allowYNow, _allowYPrev;

    Transform _lfHum, _rfHum;

    float _feetGroundY;
    bool _hasGroundBase;

    float _frozenY;

    int _bypassCounter = 0;
    int _hardLockCounter = 0;

    // ===== 公開読み取り用（PushControllerから参照）=====
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
        _anim = animatorOverride
                ? animatorOverride
                : (GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>() ?? GetComponentInParent<Animator>());

        if (root == null) root = _anim ? _anim.transform : transform;

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
        bool byTag = false;
        bool byBool = false;

        if (_anim)
        {
            if (LayerValid() && !string.IsNullOrEmpty(allowYStateTag))
            {
                var st = _anim.GetCurrentAnimatorStateInfo(layerIndex);
                var nst = _anim.GetNextAnimatorStateInfo(layerIndex);
                byTag = st.IsTag(allowYStateTag) || (_anim.IsInTransition(layerIndex) && nst.IsTag(allowYStateTag));
            }
            if (!string.IsNullOrEmpty(allowYBoolParam))
                byBool = _anim.GetBool(_allowYBoolHash);
        }

        _allowYPrev = _allowYNow;
        _allowYNow = forceClimbOverride || byTag || byBool;

        if (_allowYPrev && !_allowYNow)
        {
            if (rebaseOnClimbEnd) RebaseFeetGround("ClimbEnd");
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

        Vector3 d = _anim.deltaPosition;

        // スナップ直後ロック：下方向成分は捨てる
        if (_hardLockCounter > 0 && d.y < 0f) d.y = 0f;

        if (!_allowYNow) d.y = 0f;

        Vector3 next = before + d;

        float pushRM = 0f;
        if (_hasGroundBase)
            pushRM = RequiredPushUpToKeepFeetAboveGround(next.y);
        if (pushRM > 0f) next.y += pushRM;

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

        foreach (var kv in _initialScales)
            if (kv.Key) kv.Key.localScale = kv.Value;

        if (_allowYNow)
        {
            LogFrame("LateUpdate", before, transform.position, Vector3.zero, 0f, note: "WhileClimb");
            if (_hardLockCounter > 0) _hardLockCounter--;
            return;
        }

        if (_bypassCounter > 0)
        {
            _bypassCounter--;
            if (verboseLog) Debug.Log($"[FBS] Bypass after rebase... {_bypassCounter}f left");
            if (_hardLockCounter > 0) _hardLockCounter--;
            return;
        }

        float pushLate = 0f;
        if (_hasGroundBase && TryGetFootYs(out float yL, out float yR, out _))
        {
            float feetY = PickFeetY(yL, yR);
            float want = _feetGroundY;
            float diff = want - feetY;

            // スナップ直後ロック：下げ補正は無視
            if (_hardLockCounter > 0 && diff < 0f) diff = 0f;

            if (Mathf.Abs(diff) > 1e-6f)
            {
                ApplyDeltaY(diff);
                pushLate = diff;
            }

            if (diff > 0f) _frozenY = Mathf.Max(_frozenY, transform.position.y);
        }
        else
        {
            float diff = Mathf.Max(0f, _frozenY - transform.position.y);
            if (Mathf.Abs(diff) > 1e-6f) ApplyDeltaY(diff);
        }

        LogFrame("LateUpdate", before, transform.position, Vector3.zero, pushLate, note: Mathf.Abs(pushLate) > 0f ? "HardLock" : null);

        if (_hardLockCounter > 0) _hardLockCounter--;
    }

    // —— 公開API —— //
    public bool TryGetFeetGroundY(out float y) { y = _feetGroundY; return _hasGroundBase; }

    public void SetGroundYAndSnap(float groundY, string reason = "External")
    {
        _feetGroundY = groundY;
        _hasGroundBase = true;

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

        _hardLockCounter = Mathf.Max(_hardLockCounter, lockFramesAfterExternalSnap);
        if (verboseLog) Debug.Log($"[FBS] SetGroundYAndSnap({reason}) groundY={_feetGroundY:F4} lock={_hardLockCounter}f");
    }

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
            baseY = lf ? yL : yR;
        }

        _feetGroundY = baseY + footBaseOffset + safetyMargin;
        _hasGroundBase = true;

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

    // 移動適用（CC.Move優先）
    void ApplyDeltaY(float diff)
    {
        if (Mathf.Abs(diff) < 1e-6f) return;
        var cc = GetComponent<CharacterController>();
        if (cc && cc.enabled) cc.Move(new Vector3(0f, diff, 0f));
        else
        {
            var p = transform.position; p.y += diff; transform.position = p;
        }
    }

    // —— 足Y取得／補助 —— //
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

    static bool IsValidFootPair(Transform l, Transform r)
    {
        if (l == null || r == null) return false;
        if (l == r) return false;
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
            default: return 0.5f * (yL + yR);
        }
    }

    bool LayerValid() => _anim && layerIndex >= 0 && layerIndex < _anim.layerCount;

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
