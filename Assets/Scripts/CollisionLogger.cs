using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CollisionLogger : MonoBehaviour
{
    [Header("What to log")]
    public bool includeTriggers = true;        // Trigger���E��
    public bool logEveryHit = false;           // ����̐ڐG���o���i�I�t�Ȃ珉��̂݁j
    public bool logSummaryOnDisable = true;    // ������/�j�����Ɉꗗ�\��

    [Header("Filter")]
    public LayerMask layerMask = ~0;           // ���O�Ώۂ̃��C���[�i�f�t�H���g=�S���j

    private readonly HashSet<GameObject> _hitObjects = new HashSet<GameObject>();

    // �����ՓˁiRigidbody�~Collider�j
    private void OnCollisionEnter(Collision collision)
    {
        TryLog(collision.gameObject, "CollisionEnter");
    }

    // Trigger�N��
    private void OnTriggerEnter(Collider other)
    {
        if (!includeTriggers) return;
        TryLog(other.gameObject, "TriggerEnter");
    }

    // CharacterController�p
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        TryLog(hit.collider.gameObject, "ControllerHit");
    }

    private void Update()
    {
        // F2�Ń��Z�b�g�i�C�Ӂj
        if (Input.GetKeyDown(KeyCode.F2)) Clear();
    }

    private void TryLog(GameObject go, string source)
    {
        if (!go) return;
        if (((1 << go.layer) & layerMask.value) == 0) return; // ���C���[�t�B���^

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
