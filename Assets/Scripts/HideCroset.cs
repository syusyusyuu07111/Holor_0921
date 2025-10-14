using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class HideCroset : MonoBehaviour
{
    public Transform Player;                             // プレイヤー本体
    public List<Transform> CrosetLists = new List<Transform>();   // クローゼット群（Transform）を入れておく
    public bool hide = false;                             // 隠れているかどうか
    public InputSystem_Actions Input;                     // 新InputSystem生成クラス

    // --------------- 調整用（Inspectorから変更可） ---------------
    public float OffsetForward = 0.30f;                   // クローゼットの奥方向（+で内側へ）
    public float OffsetRight = 0.00f;                     // 右方向の微調整
    public float OffsetUp = 0.00f;                        // 上方向の微調整
    public float InteractRadius = 1.6f;                   // 近接判定の半径
    public MonoBehaviour[] MovementScriptsToDisable;      // 隠れている間だけ無効化したい移動系スクリプト

    // --------------- 内部状態 ---------------
    private Transform _currentCloset;                      // 今入っているクローゼット
    private Vector3 _cachedPos;                            // 入る前の位置
    private Vector3 _lockedInsidePos;                      // 隠れている間に固定する位置
    private Collider[] _playerCols;                        // プレイヤー側コライダー
    private readonly List<Collider> _closetCols = new List<Collider>(); // クローゼットのコライダー群

    private void Awake()
    {
        Input = new InputSystem_Actions();                 // 生成
        if (!Player) Player = transform;                   // 未設定なら自身
        _playerCols = Player.GetComponentsInChildren<Collider>(true); // 衝突無視用
    }

    private void OnEnable()
    {
        Input.Player.Enable();                             // アクション有効化
        // --------------- 入力購読 ---------------
        Input.Player.Interact.performed += OnInterect;     // 綴り Interect
    }

    private void OnDisable()
    {
        // --------------- 入力購読解除 ---------------
        Input.Player.Interact.performed -= OnInterect;     // 解除
        Input.Player.Disable();
    }

    private void Update()
    {
        if (hide) Player.position = _lockedInsidePos;      // 隠れている間は位置を固定（transform.positionのみ）
    }

    // --------------- Interect押下 ---------------
    private void OnInterect(InputAction.CallbackContext _)
    {
        if (hide) { ExitCloset(); return; }               // 既に隠れていれば出る

        Transform closet = FindNearestCloset();           // 最寄りクローゼット検索
        if (closet) EnterCloset(closet);                  // 見つかれば入る
    }

    // --------------- 最寄りクローゼット検索 ---------------
    private Transform FindNearestCloset()
    {
        float best = float.MaxValue;                      // 最短距離
        Transform pick = null;                            // 候補

        for (int i = 0; i < CrosetLists.Count; i++)
        {
            var t = CrosetLists[i];
            if (!t) continue;
            float d = (Player.position - GetClosetCenter(t)).sqrMagnitude; // 中心距離
            if (d < best && d <= InteractRadius * InteractRadius) { best = d; pick = t; } // 半径内のみ
        }

        if (!pick)                                        // フォールバック（リスト未設定/届かない時）
        {
            Collider[] hits = Physics.OverlapSphere(Player.position, InteractRadius, ~0, QueryTriggerInteraction.Collide); // 全Layer
            foreach (var h in hits)
            {
                Transform t = h.transform;
                if (CrosetLists.Count > 0 && !CrosetLists.Contains(t)) continue; // リスト運用時はリスト外を無視
                float d = (Player.position - GetClosetCenter(t)).sqrMagnitude;
                if (d < best) { best = d; pick = t; }
            }
        }
        return pick;                                      // 見つからなければnull
    }

    // --------------- 入る（positionのみ） ---------------
    private void EnterCloset(Transform closet)
    {
        _currentCloset = closet;                          // 現在のクローゼット
        _cachedPos = Player.position;                     // 出る位置＝今の位置

        _closetCols.Clear();                              // クローゼットのCollider収集
        closet.GetComponentsInChildren(true, _closetCols);
        ToggleIgnoreClosetCollision(true);                // 衝突無視ON

        Vector3 center = GetClosetCenter(closet);         // クローゼット中心
        Vector3 offset =
              (closet.forward * -OffsetForward)           // 奥方向へ（+で内側に入る）
            + (closet.right * OffsetRight)                // 右方向微調整
            + (Vector3.up * OffsetUp);                    // 上方向微調整

        Vector3 targetPos = center + offset;              // 目標位置
        Player.position = targetPos;                      // ワープ（positionのみ）
        _lockedInsidePos = targetPos;                     // ロック座標

        SetMovementEnabled(false);                        // 移動系スクリプトを無効化
        hide = true;                                      // 隠れ状態ON
    }

    // --------------- 出る（positionのみ） ---------------
    private void ExitCloset()
    {
        Player.position = _cachedPos;                     // 入る前へ戻す
        ToggleIgnoreClosetCollision(false);               // 衝突無視OFF
        _closetCols.Clear();

        SetMovementEnabled(true);                         // 移動系スクリプトを再有効化
        _currentCloset = null;                            // クリア
        hide = false;                                     // 隠れ状態OFF
    }

    // --------------- クローゼット中心取得 ---------------
    private Vector3 GetClosetCenter(Transform closet)
    {
        if (closet && closet.TryGetComponent<Collider>(out var col)) return col.bounds.center; // Collider優先
        return closet ? closet.position : Player.position;                                      // フォールバック
    }

    // --------------- 衝突無視オン/オフ ---------------
    private void ToggleIgnoreClosetCollision(bool ignore)
    {
        if (_playerCols == null || _playerCols.Length == 0) return; // プレイヤーにColliderが無ければ何もしない
        for (int i = 0; i < _closetCols.Count; i++)
        {
            var c = _closetCols[i];
            if (!c) continue;
            for (int j = 0; j < _playerCols.Length; j++)
            {
                var pc = _playerCols[j];
                if (!pc) continue;
                Physics.IgnoreCollision(pc, c, ignore);   // 双方の衝突を無視/解除
            }
        }
    }

    // --------------- 移動スクリプトの有効/無効 ---------------
    private void SetMovementEnabled(bool enabled)
    {
        if (MovementScriptsToDisable == null) return;     // Inspector未設定なら何もしない
        for (int i = 0; i < MovementScriptsToDisable.Length; i++)
        {
            var m = MovementScriptsToDisable[i];
            if (m) m.enabled = enabled;                   // 有効/無効を切り替え
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Player) return;
        Gizmos.DrawWireSphere(Player.position, InteractRadius); // 範囲確認用
    }
}
