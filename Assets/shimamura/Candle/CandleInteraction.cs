using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーが蝋燭を消す操作を行うスクリプト
/// </summary>
public class CandleInteraction : MonoBehaviour
{
    [Header("Ray設定")]
    [SerializeField] private Transform _rayOrigin;
    [SerializeField] private float _interactRange = 2f;    // 蝋燭を消せる距離
    [SerializeField] private LayerMask _candleLayer;       // 蝋燭のレイヤー

    [Header("入力設定")]
    [SerializeField] private InputActionReference _interactActionRef;
    private InputAction _interactAction;

    [Header("UI設定")]
    [SerializeField] private GameObject _interactionText;
    private Candle _currentCandle; // 今レイが当たっている蝋燭
    [SerializeField] private GameObject _pintsText;


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
            return;
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

    private void Update()
    {
        UpdateRaycast(); // 常に蝋燭を検出してUIを制御
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (_currentCandle != null)
        {
            _currentCandle.CandlePutOut();
            _interactionText?.SetActive(false); // 消したらUIも消す
        }
    }

    private void UpdateRaycast()
    {
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);

        // 全レイヤーに当ててOK（タグで絞る）
        if (Physics.Raycast(ray, out RaycastHit hit, _interactRange, ~0, QueryTriggerInteraction.Collide))
        {
            // タグが "Candle" なら反応
            if (hit.collider.CompareTag("Candle"))
            {
                Candle candle = hit.collider.GetComponentInParent<Candle>();
                if (candle != null)
                {
                    _currentCandle = candle;

                    // UIを表示
                    if (_interactionText != null && !_interactionText.activeSelf)
                        _interactionText.SetActive(true);

                    return; // 現在蝋燭を見ている
                }
            }
            if (hit.collider.CompareTag("paintings_sea") || hit.collider.CompareTag("paintings_sunflower") || hit.collider.CompareTag("paintings_swing"))
            {
                if (hit.collider.GetComponentInParent<Painting>() != null)
                {
                    if (_pintsText != null && !_pintsText.activeSelf)
                        _pintsText.SetActive(true);
                    return;
                }
            }

        }

        // ここに来たら蝋燭に当たっていない
        _currentCandle = null;
        if (_interactionText != null && _interactionText.activeSelf)
            _interactionText.SetActive(false);

        if (_pintsText != null && _pintsText.activeSelf)
            _interactionText.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        if (_rayOrigin == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _interactRange);
        Gizmos.DrawSphere(_rayOrigin.position + _rayOrigin.forward * _interactRange, 0.05f);
    }
}

