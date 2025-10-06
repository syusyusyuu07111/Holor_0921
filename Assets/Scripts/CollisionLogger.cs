using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CollisionLogger : MonoBehaviour
{
    [Header("What to log")]
    public bool includeTriggers = true;        // Triggerも拾う
    public bool logEveryHit = false;           // 毎回の接触も出す（オフなら初回のみ）
    public bool logSummaryOnDisable = true;    // 無効化/破棄時に一覧表示

    [Header("Filter")]
    public LayerMask layerMask = ~0;           // ログ対象のレイヤー（デフォルト=全部）

    private readonly HashSet<GameObject> _hitObjects = new HashSet<GameObject>();

    // 物理衝突（Rigidbody×Collider）
    private void OnCollisionEnter(Collision collision)
    {
        TryLog(collision.gameObject, "CollisionEnter");
    }

    // Trigger侵入
    private void OnTriggerEnter(Collider other)
    {
        if (!includeTriggers) return;
        TryLog(other.gameObject, "TriggerEnter");
    }

    // CharacterController用
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        TryLog(hit.collider.gameObject, "ControllerHit");
    }

    private void Update()
    {
        // F2でリセット（任意）
        if (Input.GetKeyDown(KeyCode.F2)) Clear();
    }

    private void TryLog(GameObject go, string source)
    {
        if (!go) return;
        if (((1 << go.layer) & layerMask.value) == 0) return; // レイヤーフィルタ

        bool isNew = _hitObjects.Add(go);
        if (isNew)
        {
            Debug.Log($"[CollisionLogger] New hit: {go.name} (layer={LayerMask.LayerToName(go.layer)}, via {source})", go);
        }
        if (logEveryHit)
        {
            Debug.Log($"[CollisionLogger] Hit: {go.name} via {source}", go);
        }
    }

    public void Clear()
    {
        _hitObjects.Clear();
        Debug.Log("[CollisionLogger] Cleared logged objects.");
    }

    private void OnDisable()
    {
        if (!logSummaryOnDisable) return;
        if (_hitObjects.Count == 0)
        {
            Debug.Log("[CollisionLogger] No hits.");
            return;
        }
        string names = string.Join(", ", _hitObjects.Where(o => o).Select(o => o.name));
        Debug.Log($"[CollisionLogger] Summary ({_hitObjects.Count}): {names}");
    }
}
