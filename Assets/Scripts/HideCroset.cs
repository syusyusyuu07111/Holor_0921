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

    // ★ 追加：クローゼット内の“浮き”と重力制御
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

    // ★ 追加：Rigidbody状態の退避
    private Rigidbody[] _playerRBs;
    private readonly List<bool> _rbPrevUseGravity = new List<bool>();
    private readonly List<bool> _rbPrevKinematic = new List<bool>();

    private void Awake()
    {
        Input = new InputSystem_Actions();
        if (!Player) Player = transform;

        _playerCols = Player.GetComponentsInChildren<Collider>(true);
        _playerRBs = Player.GetComponentsInChildren<Rigidbody>(true);

        if (PromptText)
        {
            PromptText.text = "";
            PromptText.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        Input.Player.Enable();
        Input.Player.Interact.performed += OnInterect; // 「E」など
    }

    private void OnDisable()
    {
        Input.Player.Interact.performed -= OnInterect;
        Input.Player.Disable();
    }

    private void Update()
    {
        // 隠れ中は位置を固定（物理は触らず position のみ）
        if (hide) Player.position = _lockedInsidePos;

        // 近接案内UIの制御
        UpdateHidePromptUI();
    }

    // 近接案内UIと初回イベント
    private void UpdateHidePromptUI()
    {
        if (!PromptText) return;

        if (hide)
        {
            if (PromptText.gameObject.activeSelf) PromptText.gameObject.SetActive(false);
            return;
        }

        var closet = FindNearestCloset();
        bool canHideHere = closet && (Player.position - GetClosetCenter(closet)).sqrMagnitude <= InteractRadius * InteractRadius;

        if (canHideHere)
        {
            PromptText.text = string.IsNullOrEmpty(PromptMessage) ? "【E】隠れる" : PromptMessage;

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

        // リスト未設定なら周囲サーチ（任意）
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
            + (Vector3.up * (OffsetUp + HiddenYOffset)); // ★ 隠れ時はYをさらに持ち上げ

        Vector3 targetPos = center + offset;
        Player.position = targetPos;
        _lockedInsidePos = targetPos;

        // ★ 重力/運動制御（子含むRigidbody）
        if (DisableGravityWhileHidden || MakeKinematicWhileHidden)
        {
            CacheAndApplyRBState(disableGravity: DisableGravityWhileHidden, makeKinematic: MakeKinematicWhileHidden);
        }

        SetMovementEnabled(false);
        hide = true;

        if (PromptText) PromptText.gameObject.SetActive(false);
    }

    // 出る（元の位置へ）
    private void ExitCloset()
    {
        Player.position = _cachedPos;
        ToggleIgnoreClosetCollision(false);
        _closetCols.Clear();

        // ★ 重力/運動を復元
        RestoreRBState();

        SetMovementEnabled(true);
        _currentCloset = null;
        hide = false;
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

    // ★ Rigidbody状態のキャッシュ＆適用
    // ★ Rigidbody状態のキャッシュ＆適用
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

            if (disableGravity) rb.useGravity = false;
            if (makeKinematic) rb.isKinematic = true;

            // 慣性を止める（その場で固定）
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }
    }


    // ★ Rigidbody状態の復元
    private void RestoreRBState()
    {
        if (_playerRBs == null || _playerRBs.Length == 0) return;

        for (int i = 0; i < _playerRBs.Length; i++)
        {
            var rb = _playerRBs[i];
            if (!rb) continue;

            // セーフティ：記録がなければデフォルトに戻す
            bool useG = (i < _rbPrevUseGravity.Count) ? _rbPrevUseGravity[i] : true;
            bool kin = (i < _rbPrevKinematic.Count) ? _rbPrevKinematic[i] : false;

            rb.useGravity = useG;
            rb.isKinematic = kin;
        }

        _rbPrevUseGravity.Clear();
        _rbPrevKinematic.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (!Player) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Player.position, InteractRadius);
    }
}
