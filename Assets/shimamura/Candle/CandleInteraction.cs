using UnityEngine;
using UnityEngine.InputSystem;

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

    [Header("チュートリアル参照")]
    [SerializeField] private SecondRoomTutorial _secondRoomTutorial; // ← 追加（インスペクタでアサイン）

    private void OnEnable()
    {
        if (_interactActionRef == null) return;

        _interactAction = _interactActionRef.action;
        _interactAction.Enable();
        _interactAction.performed += OnInteract;
    }

    private void OnDisable()
    {
        if (_interactAction == null) return;

        _interactAction.performed -= OnInteract;
        _interactAction.Disable();
    }

    private void Update()
    {
        UpdateRaycast(); // 常に蝋燭を検出してUIを制御
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (_currentCandle == null) return;

        // ★ 本を全部集め終わる前（チュートリアル2未満）は蝋燭イベント封印
        if (_secondRoomTutorial != null && !_secondRoomTutorial.CanTriggerCandleEvent)
        {
            // 必要ならここで「まだ何か足りない気がする…」などの演出も可
            return;
        }

        // 解禁済みなら普通に消す
        _currentCandle.CandlePutOut();
        _interactionText?.SetActive(false); // 消したらUIも消す
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
                // ★ UI自体も出したくないならここでもガード
                if (_secondRoomTutorial != null && !_secondRoomTutorial.CanTriggerCandleEvent)
                {
                    HideAllUI();
                    _currentCandle = null;
                    return;
                }

                Candle candle = hit.collider.GetComponentInParent<Candle>();
                if (candle != null)
                {
                    if (candle.IsPutOut)
                    {
                        HideAllUI();
                        _currentCandle = null;
                        return;
                    }

                    _currentCandle = candle;

                    // UIを表示
                    if (_interactionText != null && !_interactionText.activeSelf)
                        _interactionText.SetActive(true);

                    return; // 現在蝋燭を見ている
                }
            }

            if (hit.collider.CompareTag("paintings_sea") ||
                hit.collider.CompareTag("paintings_sunflower") ||
                hit.collider.CompareTag("paintings_swing"))
            {
                if (hit.collider.GetComponentInParent<Painting>() != null)
                {
                    if (_pintsText != null && !_pintsText.activeSelf)
                        _pintsText.SetActive(true);
                    return;
                }
            }
        }

        // ここに来たら蝋燭/絵に当たっていない
        HideAllUI();
        _currentCandle = null;
    }

    private void HideAllUI()
    {
        if (_interactionText != null && _interactionText.activeSelf)
            _interactionText.SetActive(false);

        if (_pintsText != null && _pintsText.activeSelf)
            _pintsText.SetActive(false);   // ← ここバグ直し！_pintsText を消す
    }

    private void OnDrawGizmosSelected()
    {
        if (_rayOrigin == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _interactRange);
        Gizmos.DrawSphere(_rayOrigin.position + _rayOrigin.forward * _interactRange, 0.05f);
    }
}
