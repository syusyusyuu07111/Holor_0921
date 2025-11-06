using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーが蝋燭を消す操作を行うスクリプト
/// </summary>
public class CandleInteraction : MonoBehaviour
{
    [SerializeField] private Transform _rayOrigin;
    [SerializeField] private float _interactRange = 2f;    // 蝋燭を消せる距離
    [SerializeField] private LayerMask _candleLayer;       // 蝋燭のレイヤー

    [SerializeField] private InputActionReference _interactActionRef;

    private InputAction _interactAction;

    private void OnEnable()
    {
        if (_interactActionRef != null)
        {
            _interactAction = _interactActionRef.action; // アセットから取得
            _interactAction.Enable();
            _interactAction.performed += OnInteract;
        }
        else
        {
            Debug.LogError("CandleInteraction に InputActionReference が設定されていません！");
        }
    }

    private void OnDisable()
    {
        if (_interactAction != null)
        {
            _interactAction.performed -= OnInteract;
            _interactAction.Disable();
        }
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
     
        TryPutOutCandle();
    }

    private void TryPutOutCandle()
    {
        Debug.Log($"LayerMask: {_candleLayer.value}");

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _interactRange, _candleLayer, QueryTriggerInteraction.Collide))
        {
            Debug.Log($"レイが {hit.collider.name} に当たった (Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)})");
            Candle candle = hit.collider.GetComponentInParent<Candle>();
            if (candle != null)
            {
                candle.CandlePutOut();
                Debug.Log("蝋燭を消した！");
            }
            else
            {
                Debug.Log("Candleスクリプトは付いていなかった");
            }
        }
        else
        {
            Debug.Log("何にも当たらなかった");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_rayOrigin == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _interactRange);
        Gizmos.DrawSphere(_rayOrigin.position + _rayOrigin.forward * _interactRange, 0.05f);
    }
}

