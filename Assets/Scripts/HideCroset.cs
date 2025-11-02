using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.Events;

public class HideCroset : MonoBehaviour
{
    public Transform Player;                                    // プレイヤー
    public List<Transform> CrosetLists = new List<Transform>(); // クローゼット候補
    public bool hide = false;                                   // 隠れ中か
    public InputSystem_Actions Input;                           // 新InputSystem

    [Header("各クローゼットに対応するドア（CrosetLists と同じ順番で並べる）")]
    public List<Transform> DoorList = new List<Transform>();    // i番目のクローゼットのドアは DoorList[i]

    [Header("ドア演出（共通設定）")]
    public bool UseRotationInstead = false;                     // false: 平行移動 / true: 回転
    public Vector3 ShiftAxis = Vector3.right;                   // 平行移動のローカル軸
    public float ShiftValue = 0.3f;                             // 平行移動量
    public Vector3 RotateAxis = Vector3.up;                     // 回転のローカル軸
    public float RotateDegrees = 12f;                           // 回転角
    public float ShiftDuration = 0.15f;                         // 補間時間（入った時だけ使用）
    public AnimationCurve ShiftCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("位置調整（Inspectorで変更可・実行中も可）")]
    public float OffsetForward = 0.30f;                         // 奥方向（+で内側）
    public float OffsetRight = 0.00f;                           // 右
    public float OffsetUp = 0.00f;                              // 上（ベース）
    public float InteractRadius = 1.6f;                         // 隠れられる半径
    public MonoBehaviour[] MovementScriptsToDisable;            // 隠れ中だけ無効化する移動系

    [Header("UI（隠れ案内）")]
    public TextMeshProUGUI PromptText;                          // 「【E】隠れる」
    public string PromptMessage = "【E】隠れる";

    [Header("イベント（Tutorialが購読）")]
    public UnityEvent OnFirstHidePromptShown;                   // 初めて案内が出た瞬間

    [Header("隠れ中のテキスト差し替え")]
    [TextArea] public string HiddenPromptMessage = "……（息を潜める）";
    public bool RewriteTextWhileHidden = true;                  // 隠れ開始で差し替える
    public bool RestoreTextOnExit = true;                       // 解除で元に戻す
    public UnityEvent<string> OnPromptRewritten;                // 差し替え時コールバック（任意）
    public UnityEvent<string> OnPromptRestored;                 // 復元時コールバック（任意）

    [Header("クローゼット内の浮き/重力")]
    public float HiddenYOffset = 0.20f;                         // 隠れ時のY持ち上げ（台座ぶん）
    public bool DisableGravityWhileHidden = true;               // 隠れ中は重力を切る
    public bool MakeKinematicWhileHidden = true;                // 隠れ中はkinematic化（任意）

    // ── カメラ連携 ──
    [Header("カメラ参照（隠れ中演出）")]
    public TPSCamera TPS;                                       // 任意：未割り当てなら何もしない

    [Header("隠れ中のカメラ距離（差分方式：任意）")]
    public bool EnableHiddenDistance = true;
    [Min(0f)] public float HiddenDistanceDelta = 1.4f;          // Distance からの減算量
    public float HiddenDistanceLerp = 12f;

    [Header("覗き前進（任意）")]
    public bool EnableFrontWhenHidden = true;
    [Min(0f)] public float PeekForward = 0.20f;
    public float PeekForwardLerp = 12f;

    [Header("カメラ衝突（隠れ中は無効化）")]
    public bool DisableCameraCollisionWhileHidden = true;

    // ★ Door を“隙間アンカー”に使う
    [Header("★ 隙間アンカー：Door を使う")]
    public bool UseDoorAsGap = true;                             // true: Door をアンカーに
    public Vector3 DoorGapLocalOffset = new Vector3(0f, 0f, 0.02f); // ドアローカル位置補正（前に少し）
    public Vector3 DoorGapForwardAxis = Vector3.forward;         // 「穴の向き」に使うローカル軸
    public bool InvertDoorGapFacing = false;                     // 反転（奥/手前の切替）
    [Tooltip("穴の方向をさらに調整する角度（度数）。+で右へ、-で左へ。")]
    public float DoorGapYawOffsetDeg = 0f;                       // 向きの微調整

    // 旧：プレイヤー基準アンカー（残すだけ）
    [Header("（旧）プレイヤー基準アンカー")]
    public bool UseGapAnchorWhileHidden = false;
    public float GapOffsetX = 0.00f;
    public float GapOffsetZ = 0.20f;
    public float GapOffsetY = 0.00f;

    [Header("★ 視界制限（隠れ中だけ前方180°）")]
    public float HiddenYawHalfAngle = 90f;                      // 左右の半角（90=180°）

    [Header("隠れ中の見回し")]
    public bool AllowLookAroundWhileHidden = true;              // 穴方向を中心に±90°

    [Header("隠れ中のプレイヤー可視性")]
    public bool HidePlayerWhileHidden = true;                   // 隠れている間はプレイヤー非表示

    // ───── 内部退避 ─────
    private bool _prevKeepFixed = false;
    private bool _keepFixedSaved = false;

    // ───────── 内部状態 ─────────
    private Transform _currentCloset;
    private int _currentClosetIndex = -1;
    private Transform _currentDoor;

    private Vector3 _cachedPos;
    private Vector3 _lockedInsidePos;
    private Collider[] _playerCols;
    private readonly List<Collider> _closetCols = new List<Collider>();
    private bool _hidePromptEverShown = false;

    // Rigidbody状態の退避
    private Rigidbody[] _playerRBs;
    private readonly List<bool> _rbPrevUseGravity = new List<bool>();
    private readonly List<bool> _rbPrevKinematic = new List<bool>();

    // ドアの初期姿勢キャッシュ＆補間
    private readonly Dictionary<Transform, Vector3> _doorPosDefault = new();
    private readonly Dictionary<Transform, Quaternion> _doorRotDefault = new();
    private Coroutine _doorTween;

    // プレイヤー見た目の退避
    private Renderer[] _playerRenderers;                        // 子含む全Renderer
    private readonly List<bool> _rendererPrevEnabled = new List<bool>(); // enabledの退避

    private void Awake()
    {
        Input = new InputSystem_Actions();
        if (!Player) Player = transform;

        _playerCols = Player.GetComponentsInChildren<Collider>(true);
        _playerRBs = Player.GetComponentsInChildren<Rigidbody>(true);
        _playerRenderers = Player.GetComponentsInChildren<Renderer>(true); // ★ 追加

        // DoorList の初期ローカル姿勢をキャッシュ
        for (int i = 0; i < DoorList.Count; i++)
        {
            var d = DoorList[i];
            if (!d) continue;
            if (!_doorPosDefault.ContainsKey(d)) _doorPosDefault.Add(d, d.localPosition);
            if (!_doorRotDefault.ContainsKey(d)) _doorRotDefault.Add(d, d.localRotation);
        }

        if (PromptText)
        {
            PromptText.text = "";
            PromptText.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        Input.Player.Enable();
        Input.Player.Interact.started += OnInterect; // 「E」など（押した瞬間）
    }

    private void OnDisable()
    {
        Input.Player.Interact.started -= OnInterect;
        Input.Player.Disable();

        // 隠れたまま無効化された場合でも必ず全ドアをリセット（shift=0）
        ResetAllDoorsImmediate();

        // ★ 無効化時でも見た目は戻す
        if (HidePlayerWhileHidden) SetPlayerVisible(true);

        // カメラ設定の復帰
        if (TPS)
        {
            TPS.UseHiddenDistance = false;
            TPS.AllowFrontWhenHidden = false;
            TPS.UseHiddenAnchor = false;
            TPS.EnableHiddenYawClamp = false;
            TPS.UseHiddenLookAt = false;
            TPS.HiddenLookAt = null;

            if (_keepFixedSaved)
            {
                TPS.KeepFixedDistance = _prevKeepFixed;
                _keepFixedSaved = false;
            }
        }
    }

    private void OnDestroy()
    {
        // 念のため：破棄時も全ドアをリセット
        ResetAllDoorsImmediate();
    }

    private void Update()
    {
        // 隠れ中は位置を固定（物理は触らず position のみ）
        if (hide) Player.position = _lockedInsidePos;

        // 近接案内UIの制御（表示/非表示）
        UpdateHidePromptUI();
    }

    // 近接案内UIと初回イベント
    private void UpdateHidePromptUI()
    {
        if (!PromptText) return;

        if (hide)
        {
            // 隠れ中は「表示を消さない」。差し替え文面を表示し続ける。
            if (RewriteTextWhileHidden)
            {
                string next = string.IsNullOrEmpty(HiddenPromptMessage) ? "……" : HiddenPromptMessage;
                if (PromptText.text != next)
                {
                    PromptText.text = next;
                    OnPromptRewritten?.Invoke(next);
                }
            }
            if (!PromptText.gameObject.activeSelf) PromptText.gameObject.SetActive(true);
            return;
        }

        // 非隠れ時
        var closet = FindNearestCloset(out int idx);
        bool canHideHere = closet && (Player.position - GetClosetCenter(closet)).sqrMagnitude <= InteractRadius * InteractRadius;

        if (canHideHere)
        {
            string next = string.IsNullOrEmpty(PromptMessage) ? "【E】隠れる" : PromptMessage;
            if (PromptText.text != next) PromptText.text = next;

            if (!PromptText.gameObject.activeSelf)
            {
                PromptText.gameObject.SetActive(true);
                if (!_hidePromptEverShown)
                {
                    _hidePromptEverShown = true;
                    OnFirstHidePromptShown?.Invoke();
                }
            }
        }
        else
        {
            if (PromptText.gameObject.activeSelf) PromptText.gameObject.SetActive(false);
        }
    }

    // Interact 入力
    private void OnInterect(InputAction.CallbackContext _)
    {
        if (hide) { ExitCloset(); return; }

        Transform closet = FindNearestCloset(out int idx);
        if (closet && (Player.position - GetClosetCenter(closet)).sqrMagnitude <= InteractRadius * InteractRadius)
        {
            EnterCloset(closet, idx);
        }
    }

    // 最寄りクローゼット検索（インデックスも返す）
    private Transform FindNearestCloset(out int closetIndex)
    {
        float best = float.MaxValue;
        Transform pick = null;
        closetIndex = -1;

        for (int i = 0; i < CrosetLists.Count; i++)
        {
            var t = CrosetLists[i];
            if (!t) continue;
            float d = (Player.position - GetClosetCenter(t)).sqrMagnitude;
            if (d < best) { best = d; pick = t; closetIndex = i; }
        }

        // リスト未設定なら周囲サーチ（この場合は配列対応不可）
        if (!pick && CrosetLists.Count == 0)
        {
            Collider[] hits = Physics.OverlapSphere(Player.position, InteractRadius, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
            {
                var t = h.transform;
                float d = (Player.position - GetClosetCenter(t)).sqrMagnitude;
                if (d < best) { best = d; pick = t; closetIndex = -1; }
            }
        }
        return pick;
    }

    // 入る（瞬間ワープ：position）
    private void EnterCloset(Transform closet, int closetIndex)
    {
        _currentCloset = closet;
        _currentClosetIndex = closetIndex;
        _cachedPos = Player.position;

        _closetCols.Clear();
        closet.GetComponentsInChildren(true, _closetCols);
        ToggleIgnoreClosetCollision(true);

        Vector3 center = GetClosetCenter(closet);
        Vector3 offset =
              (closet.forward * -OffsetForward)
            + (closet.right * OffsetRight)
            + (Vector3.up * (OffsetUp + HiddenYOffset)); // 隠れ時はYをさらに持ち上げる

        Vector3 targetPos = center + offset;
        Player.position = targetPos;
        _lockedInsidePos = targetPos;

        // 重力/運動制御（子含むRigidbody）
        if (DisableGravityWhileHidden || MakeKinematicWhileHidden)
            CacheAndApplyRBState(DisableGravityWhileHidden, MakeKinematicWhileHidden);

        SetMovementEnabled(false);
        hide = true;

        // ★ 隠れ中はプレイヤー非表示
        if (HidePlayerWhileHidden) SetPlayerVisible(false);

        // 対応ドア（＝隙間アンカー）
        _currentDoor = null;
        if (closetIndex >= 0 && closetIndex < DoorList.Count)
            _currentDoor = DoorList[closetIndex];

        if (_currentDoor)
        {
            // 初回なら初期姿勢を記録
            if (!_doorPosDefault.ContainsKey(_currentDoor)) _doorPosDefault[_currentDoor] = _currentDoor.localPosition;
            if (!_doorRotDefault.ContainsKey(_currentDoor)) _doorRotDefault[_currentDoor] = _currentDoor.localRotation;

            StartDoorShift(true); // ← 入る時だけ補間で開ける
        }

        // 隠れ開始時点で文面を差し替え、表示を維持
        if (RewriteTextWhileHidden && PromptText)
        {
            string next = string.IsNullOrEmpty(HiddenPromptMessage) ? "……" : HiddenPromptMessage;
            PromptText.text = next;
            OnPromptRewritten?.Invoke(next);
            if (!PromptText.gameObject.activeSelf) PromptText.gameObject.SetActive(true);
        }

        // ★ カメラ設定（Door を“隙間アンカー”に）
        if (TPS)
        {
            // 距離寄せ / 覗き前進
            TPS.HiddenDistanceDelta = HiddenDistanceDelta;
            TPS.HiddenDistanceLerp = HiddenDistanceLerp;
            TPS.UseHiddenDistance = EnableHiddenDistance;
            TPS.AllowFrontWhenHidden = EnableFrontWhenHidden;
            TPS.PeekForward = PeekForward;
            TPS.PeekForwardLerp = PeekForwardLerp;

            // 衝突OFF（隠れ中）
            if (DisableCameraCollisionWhileHidden)
            {
                if (!_keepFixedSaved) { _prevKeepFixed = TPS.KeepFixedDistance; _keepFixedSaved = true; }
                TPS.KeepFixedDistance = true;
            }

            // 1) アンカー＝Door
            if (UseDoorAsGap && _currentDoor != null)
            {
                TPS.UseHiddenAnchor = true;
                TPS.HiddenAnchor = _currentDoor;
                TPS.HiddenAnchorLocalOffset = DoorGapLocalOffset;

                // 2) 視界中心＝Door の向き（ローカル任意軸）
                Vector3 axis = DoorGapForwardAxis.sqrMagnitude < 0.0001f ? Vector3.forward : DoorGapForwardAxis;
                Vector3 dirW = _currentDoor.TransformDirection(axis);
                if (InvertDoorGapFacing) dirW = -dirW;

                float yawCenter = Mathf.Atan2(dirW.x, dirW.z) * Mathf.Rad2Deg;
                yawCenter += DoorGapYawOffsetDeg; // ★ Yaw微調整

                TPS.HiddenYawCenter = yawCenter;
                TPS.HiddenYawHalfAngle = Mathf.Abs(HiddenYawHalfAngle); // 例：90=前方180°
                TPS.EnableHiddenYawClamp = true;

                // 初期向きも穴方向へ
                TPS.yaw = yawCenter;

                // LookAt 固定は使わず見回し（±半角）に任せる
                TPS.UseHiddenLookAt = false;
                TPS.HiddenLookAt = null;
            }
            else
            {
                // フォールバック：旧プレイヤー基準アンカー
                if (UseGapAnchorWhileHidden)
                {
                    TPS.UseHiddenAnchor = true;
                    TPS.HiddenAnchor = Player;
                    TPS.HiddenAnchorLocalOffset = new Vector3(GapOffsetX, GapOffsetY, GapOffsetZ);
                }
                TPS.UseHiddenLookAt = false;
                TPS.HiddenLookAt = null;

                Vector3 dir = Player.forward;
                float yawCenter = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                TPS.HiddenYawCenter = yawCenter;
                TPS.HiddenYawHalfAngle = Mathf.Abs(HiddenYawHalfAngle);
                TPS.EnableHiddenYawClamp = true;
                TPS.yaw = yawCenter;
            }
        }
    }

    // 出る（元の位置へ）
    private void ExitCloset()
    {
        Player.position = _cachedPos;
        ToggleIgnoreClosetCollision(false);
        _closetCols.Clear();

        // 重力/運動を復元
        RestoreRBState();

        SetMovementEnabled(true);
        hide = false;

        // ★ 退出時に表示を元へ
        if (HidePlayerWhileHidden) SetPlayerVisible(true);

        // ★ 出たらすべてのshiftを0に（全ドアを即リセット）
        ResetAllDoorsImmediate();

        _currentCloset = null;
        _currentClosetIndex = -1;
        _currentDoor = null;

        // 解除：テキストを元の文面に戻す（表示/非表示は距離・視界ロジックに任せる）
        if (RestoreTextOnExit && PromptText)
        {
            string next = string.IsNullOrEmpty(PromptMessage) ? "【E】隠れる" : PromptMessage;
            PromptText.text = next;
            OnPromptRestored?.Invoke(next);
        }

        // カメラ設定を復帰
        if (TPS)
        {
            TPS.UseHiddenDistance = false;
            TPS.AllowFrontWhenHidden = false;
            TPS.UseHiddenAnchor = false;
            TPS.EnableHiddenYawClamp = false;
            TPS.UseHiddenLookAt = false;
            TPS.HiddenLookAt = null;

            if (_keepFixedSaved)
            {
                TPS.KeepFixedDistance = _prevKeepFixed;
                _keepFixedSaved = false;
            }
        }
    }

    // クローゼットの中心
    private Vector3 GetClosetCenter(Transform closet)
    {
        if (closet && closet.TryGetComponent<Collider>(out var col)) return col.bounds.center;
        return closet ? closet.position : Player.position;
    }

    // 衝突無視の切替
    private void ToggleIgnoreClosetCollision(bool ignore)
    {
        if (_playerCols == null || _playerCols.Length == 0) return;
        for (int i = 0; i < _closetCols.Count; i++)
        {
            var c = _closetCols[i];
            if (!c) continue;
            for (int j = 0; j < _playerCols.Length; j++)
            {
                var pc = _playerCols[j];
                if (!pc) continue;
                Physics.IgnoreCollision(pc, c, ignore);
            }
        }
    }

    // 移動系の有効/無効
    private void SetMovementEnabled(bool enabled)
    {
        if (MovementScriptsToDisable == null) return;
        for (int i = 0; i < MovementScriptsToDisable.Length; i++)
        {
            var m = MovementScriptsToDisable[i];
            if (m) m.enabled = enabled;
        }
    }

    // プレイヤー表示/非表示の切り替え
    private void SetPlayerVisible(bool visible)
    {
        if (_playerRenderers == null || _playerRenderers.Length == 0) return;

        if (visible)
        {
            // 退避した enabled を復元
            for (int i = 0; i < _playerRenderers.Length; i++)
            {
                var r = _playerRenderers[i];
                if (!r) continue;
                bool prev = (i < _rendererPrevEnabled.Count) ? _rendererPrevEnabled[i] : true;
                r.enabled = prev;
            }
            _rendererPrevEnabled.Clear();
        }
        else
        {
            // 現在の enabled を退避して全てOFF
            _rendererPrevEnabled.Clear();
            _rendererPrevEnabled.Capacity = _playerRenderers.Length;
            for (int i = 0; i < _playerRenderers.Length; i++)
            {
                var r = _playerRenderers[i];
                if (!r) { _rendererPrevEnabled.Add(true); continue; }
                _rendererPrevEnabled.Add(r.enabled);
                r.enabled = false;
            }
        }
    }

    // Rigidbody状態のキャッシュ＆適用
    private void CacheAndApplyRBState(bool disableGravity, bool makeKinematic)
    {
        if (_playerRBs == null || _playerRBs.Length == 0) return;

        _rbPrevUseGravity.Clear();
        _rbPrevKinematic.Clear();
        _rbPrevUseGravity.Capacity = _playerRBs.Length;
        _rbPrevKinematic.Capacity = _playerRBs.Length;

        for (int i = 0; i < _playerRBs.Length; i++)
        {
            var rb = _playerRBs[i];
            if (!rb) { _rbPrevUseGravity.Add(true); _rbPrevKinematic.Add(false); continue; }

            _rbPrevUseGravity.Add(rb.useGravity);
            _rbPrevKinematic.Add(rb.isKinematic);

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;

            if (disableGravity) rb.useGravity = false;
            if (makeKinematic) rb.isKinematic = true;
        }
    }

    // Rigidbody状態の復元
    private void RestoreRBState()
    {
        if (_playerRBs == null || _playerRBs.Length == 0) return;

        for (int i = 0; i < _playerRBs.Length; i++)
        {
            var rb = _playerRBs[i];
            if (!rb) continue;

            bool useG = (i < _rbPrevUseGravity.Count) ? _rbPrevUseGravity[i] : true;
            bool kin = (i < _rbPrevKinematic.Count) ? _rbPrevKinematic[i] : false;

            rb.useGravity = useG;
            rb.isKinematic = kin;
        }

        _rbPrevUseGravity.Clear();
        _rbPrevKinematic.Clear();
    }

    // ───────── ドア補間（開くときのみ使用） ─────────
    private void StartDoorShift(bool toOpenGap)
    {
        if (!_currentDoor) return;
        if (_doorTween != null) { StopCoroutine(_doorTween); _doorTween = null; }
        _doorTween = StartCoroutine(CoDoorShift(toOpenGap));
    }

    private System.Collections.IEnumerator CoDoorShift(bool toOpenGap)
    {
        // 基準姿勢
        Vector3 basePos = _doorPosDefault.TryGetValue(_currentDoor, out var p) ? p : _currentDoor.localPosition;
        Quaternion baseRot = _doorRotDefault.TryGetValue(_currentDoor, out var r) ? r : _currentDoor.localRotation;

        // 目標
        Vector3 targetPos = basePos;
        Quaternion targetRot = baseRot;

        if (toOpenGap)
        {
            if (UseRotationInstead)
            {
                Quaternion delta = Quaternion.AngleAxis(RotateDegrees, RotateAxis.normalized);
                targetRot = baseRot * delta;
            }
            else
            {
                Vector3 localOffset = ShiftAxis.normalized * ShiftValue;
                targetPos = basePos + localOffset;
            }
        }

        float dur = Mathf.Max(0f, ShiftDuration);
        AnimationCurve curve = ShiftCurve ?? AnimationCurve.EaseInOut(0, 0, 1, 1);

        float t = 0f;
        Vector3 pos0 = _currentDoor.localPosition;
        Quaternion rot0 = _currentDoor.localRotation;

        while (t < dur)
        {
            t += Time.deltaTime;
            float u = (dur <= 0f) ? 1f : Mathf.Clamp01(t / dur);
            float k = curve.Evaluate(u);

            _currentDoor.localPosition = Vector3.LerpUnclamped(pos0, targetPos, k);
            _currentDoor.localRotation = Quaternion.SlerpUnclamped(rot0, targetRot, k);
            yield return null;
        }

        _currentDoor.localPosition = targetPos;
        _currentDoor.localRotation = targetRot;
        _doorTween = null;
    }

    // ★ 全ドアの shift を 0 に（即時リセット）
    public void ResetAllDoorsImmediate()
    {
        if (_doorTween != null) { StopCoroutine(_doorTween); _doorTween = null; }

        for (int i = 0; i < DoorList.Count; i++)
        {
            var d = DoorList[i];
            if (!d) continue;

            // 既知の初期姿勢に戻す。未知の場合は“今”を初期として扱う。
            if (_doorPosDefault.TryGetValue(d, out var p)) d.localPosition = p;
            else { _doorPosDefault[d] = d.localPosition; }

            if (_doorRotDefault.TryGetValue(d, out var r)) d.localRotation = r;
            else { _doorRotDefault[d] = d.localRotation; }
        }
    }

    // デバッグ表示
    private void OnDrawGizmosSelected()
    {
        if (!Player) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Player.position, InteractRadius);
    }
}
