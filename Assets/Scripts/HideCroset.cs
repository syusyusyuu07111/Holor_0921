using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.Events;

public class HideCroset : MonoBehaviour
{
    public Transform Player;                                   // プレイヤー
    public List<Transform> CrosetLists = new List<Transform>(); // クローゼット候補
    public bool hide = false;                                   // 隠れ中か
    public InputSystem_Actions Input;                           // 新InputSystem

    [Header("各クローゼットに対応するドア（CrosetLists と同じ順番で並べる）")]
    public List<Transform> DoorList = new List<Transform>();    // 各クローゼットのドア

    [Header("ドア演出（共通設定）")]
    public bool UseRotationInstead = false;                     // false: 平行移動 / true: 回転
    public Vector3 ShiftAxis = Vector3.right;                   // 平行移動のローカル軸
    public float ShiftValue = 0.3f;                             // 平行移動量
    public Vector3 RotateAxis = Vector3.up;                     // 回転のローカル軸
    public float RotateDegrees = 12f;                           // 回転角
    public float ShiftDuration = 0.15f;                         // 補間時間
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

    // 隠れている間のテキスト差し替え（表示は消さない）
    [Header("隠れ中のテキスト差し替え")]
    [TextArea] public string HiddenPromptMessage = "……（息を潜める）";
    public bool RewriteTextWhileHidden = true;                  // 隠れ開始で差し替える
    public bool RestoreTextOnExit = true;                       // 解除で元に戻す
    public UnityEvent<string> OnPromptRewritten;                // 差し替え時コールバック（任意）
    public UnityEvent<string> OnPromptRestored;                 // 復元時コールバック（任意）

    // クローゼット内の“浮き”と重力制御
    [Header("クローゼット内の浮き/重力")]
    public float HiddenYOffset = 0.20f;                         // 隠れ時のY持ち上げ（台座ぶん）
    public bool DisableGravityWhileHidden = true;               // 隠れ中は重力を切る
    public bool MakeKinematicWhileHidden = true;                // 隠れ中はkinematic化（任意）

    // 内部
    private Transform _currentCloset;
    private Vector3 _cachedPos;
    private Vector3 _lockedInsidePos;
    private Collider[] _playerCols;
    private readonly List<Collider> _closetCols = new List<Collider>();
    private bool _hidePromptEverShown = false;

    // Rigidbody状態の退避
    private Rigidbody[] _playerRBs;
    private readonly List<bool> _rbPrevUseGravity = new List<bool>();
    private readonly List<bool> _rbPrevKinematic = new List<bool>();

    // ドア対応・補間
    private int _currentClosetIndex = -1;
    private Transform _currentDoor;
    private readonly Dictionary<Transform, Vector3> _doorPosDefault = new();
    private readonly Dictionary<Transform, Quaternion> _doorRotDefault = new();
    private Coroutine _doorTween;

    private void Awake()
    {
        Input = new InputSystem_Actions();
        if (!Player) Player = transform;

        _playerCols = Player.GetComponentsInChildren<Collider>(true);
        _playerRBs = Player.GetComponentsInChildren<Rigidbody>(true);

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
            // ★ 隠れ中は「表示を消さない」。差し替え文面を表示し続ける。
            if (RewriteTextWhileHidden)
            {
                string next = string.IsNullOrEmpty(HiddenPromptMessage) ? "……" : HiddenPromptMessage;
                if (PromptText.text != next)            // 無駄な再代入を避ける
                {
                    PromptText.text = next;
                    OnPromptRewritten?.Invoke(next);
                }
            }
            if (!PromptText.gameObject.activeSelf) PromptText.gameObject.SetActive(true);
            return;
        }

        // ===== ここから通常時（非隠れ時）=====
        var closet = FindNearestCloset();
        bool canHideHere = closet && (Player.position - GetClosetCenter(closet)).sqrMagnitude <= InteractRadius * InteractRadius;

        if (canHideHere)
        {
            // 非隠れ時は通常文面
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

        Transform closet = FindNearestCloset();
        if (closet && (Player.position - GetClosetCenter(closet)).sqrMagnitude <= InteractRadius * InteractRadius)
        {
            EnterCloset(closet);
        }
    }

    // 最寄りクローゼット検索
    private Transform FindNearestCloset()
    {
        float best = float.MaxValue;
        Transform pick = null;

        for (int i = 0; i < CrosetLists.Count; i++)
        {
            var t = CrosetLists[i];
            if (!t) continue;
            float d = (Player.position - GetClosetCenter(t)).sqrMagnitude;
            if (d < best) { best = d; pick = t; }
        }

        // リスト未設定なら周囲サーチ（インデックス対応はできないが、隠れ自体は可能）
        if (!pick && CrosetLists.Count == 0)
        {
            Collider[] hits = Physics.OverlapSphere(Player.position, InteractRadius, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
            {
                var t = h.transform;
                float d = (Player.position - GetClosetCenter(t)).sqrMagnitude;
                if (d < best) { best = d; pick = t; }
            }
        }
        return pick;
    }

    // 入る（瞬間ワープ：position）
    private void EnterCloset(Transform closet)
    {
        _currentCloset = closet;
        _cachedPos = Player.position;

        _closetCols.Clear();
        closet.GetComponentsInChildren(true, _closetCols);
        ToggleIgnoreClosetCollision(true);

        Vector3 center = GetClosetCenter(closet);
        Vector3 offset =
              (closet.forward * -OffsetForward)
            + (closet.right * OffsetRight)
            + (Vector3.up * (OffsetUp + HiddenYOffset)); //  隠れ時はYをさらに持ち上げる　オフセット

        Vector3 targetPos = center + offset;
        Player.position = targetPos;
        _lockedInsidePos = targetPos;

        // 重力/運動制御（子含むRigidbody）
        if (DisableGravityWhileHidden || MakeKinematicWhileHidden)
        {
            CacheAndApplyRBState(disableGravity: DisableGravityWhileHidden, makeKinematic: MakeKinematicWhileHidden);
        }

        SetMovementEnabled(false);
        hide = true;

        // ===== クローゼット→ドアの対応（インデックスで取得） =====
        _currentClosetIndex = CrosetLists.IndexOf(closet);
        _currentDoor = null;
        if (_currentClosetIndex >= 0 && _currentClosetIndex < DoorList.Count)
        {
            _currentDoor = DoorList[_currentClosetIndex];
            if (_currentDoor)
            {
                // まだキャッシュされてなければ保存
                if (!_doorPosDefault.ContainsKey(_currentDoor)) _doorPosDefault[_currentDoor] = _currentDoor.localPosition;
                if (!_doorRotDefault.ContainsKey(_currentDoor)) _doorRotDefault[_currentDoor] = _currentDoor.localRotation;

                // 隠れ開始：ドアを“少し開く/ずらす”
                StartDoorShift(true);
            }
        }

        // 隠れ開始時点で文面を差し替え、表示を維持
        if (RewriteTextWhileHidden && PromptText)
        {
            string next = string.IsNullOrEmpty(HiddenPromptMessage) ? "……" : HiddenPromptMessage;
            PromptText.text = next;
            OnPromptRewritten?.Invoke(next);
            if (!PromptText.gameObject.activeSelf) PromptText.gameObject.SetActive(true);
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

        // 解除時：対応ドアを元へ
        if (_currentDoor) StartDoorShift(false);

        _currentCloset = null;
        _currentClosetIndex = -1;
        _currentDoor = null;

        // ★ 解除：テキストを元の文面に戻す（表示/非表示は距離・視界ロジックに任せる）
        if (RestoreTextOnExit && PromptText)
        {
            string next = string.IsNullOrEmpty(PromptMessage) ? "【E】隠れる" : PromptMessage;
            PromptText.text = next;
            OnPromptRestored?.Invoke(next);
            // すぐ消さず、UpdateHidePromptUIの距離判定で自動的に消える
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

            // 慣性を止める（その場で固定）
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

            // 記録がなければデフォルトに戻す
            bool useG = (i < _rbPrevUseGravity.Count) ? _rbPrevUseGravity[i] : true;
            bool kin = (i < _rbPrevKinematic.Count) ? _rbPrevKinematic[i] : false;

            rb.useGravity = useG;
            rb.isKinematic = kin;
        }

        _rbPrevUseGravity.Clear();
        _rbPrevKinematic.Clear();
    }

    // ===== ドア補間 =====
    private void StartDoorShift(bool toOpenGap)
    {
        if (!_currentDoor) return;
        if (_doorTween != null) StopCoroutine(_doorTween);
        _doorTween = StartCoroutine(CoDoorShift(toOpenGap));
    }

    private System.Collections.IEnumerator CoDoorShift(bool toOpenGap)
    {
        Vector3 basePos = _doorPosDefault.TryGetValue(_currentDoor, out var p) ? p : _currentDoor.localPosition;
        Quaternion baseRot = _doorRotDefault.TryGetValue(_currentDoor, out var r) ? r : _currentDoor.localRotation;

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
            float u = dur <= 0f ? 1f : Mathf.Clamp01(t / dur);
            float k = curve.Evaluate(u);

            _currentDoor.localPosition = Vector3.LerpUnclamped(pos0, targetPos, k);
            _currentDoor.localRotation = Quaternion.SlerpUnclamped(rot0, targetRot, k);
            yield return null;
        }

        _currentDoor.localPosition = targetPos;
        _currentDoor.localRotation = targetRot;
        _doorTween = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Player) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Player.position, InteractRadius);
    }
}
