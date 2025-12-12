using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 椅子を押したり、乗る（放物線ジャンプ）を管理するスクリプト
/// </summary>
public class PushController : MonoBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private float _pushDistance = 0.2f;      // 家具を検出する距離
    [SerializeField] private float _pushSpeed = 1.5f;         // 移動速度
    [SerializeField] private LayerMask _LayerPositoin;        // 押せるオブジェクトのレイヤー

    [Header("References")]
    [SerializeField] private Transform _rayOrigin;            // レイを飛ばす起点
    [SerializeField] private Transform _player;               // プレイヤー本体（位置を動かす対象）

    [Header("Jump Settings")]
    [SerializeField] private float _jumpDuration = 0.7f;      // 飛ぶ時間（秒）
    [SerializeField] private float _jumpHeight = 0.75f;       // 放物線の高さ

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI _pushTextMeshPro;  // 椅子を押す＋乗るテキスト

    [Header("Input Actions")]
    [SerializeField] private InputActionReference _pushActionRef;   // 押すアクション
    [SerializeField] private InputActionReference _jumpActionRef;   // 乗るアクション
    private InputAction _pushAction;
    private InputAction _jumpAction;


    private Rigidbody _pushingRb = null;     // 押しているオブジェクト
    private Transform _pushingTransform = null; //Transform移動用の参照
    private Vector3 _pushDirection;          // 押す方向
    private bool _isPushing = false;         //押している間だけ動かす
    private bool _originalKinematic;

    // ジャンプ関連
    private bool _isJumping = false;         // 飛行中フラグ
    private Vector3 _jumpStart;              // ジャンプ開始位置
    private Vector3 _jumpEnd;                // ジャンプ目標位置
    private float _jumpElapsed = 0f;         // ジャンプ経過時間

    private void OnEnable()
    {
        if (_pushActionRef != null)
        {
            _pushAction = _pushActionRef.action;
            _pushAction.Enable();
            _pushAction.performed += OnPushPressed;
            _pushAction.canceled += OnPushReleased;
        }
        if (_jumpActionRef != null)
        {
            _jumpAction = _jumpActionRef.action;
            _jumpAction.Enable();
            _jumpAction.performed += OnJumpPressed;
        }
        else
        {
            Debug.LogError("CandleInteraction に InputActionReference が設定されていません！");
        }
    }

    private void OnDisable()
    {
        if (_pushAction != null)
        {
            _pushAction.performed -= OnPushPressed;
            _pushAction.canceled -= OnPushReleased;
            _pushAction.Disable();
        }

        if (_jumpAction != null)
        {
            _jumpAction.performed -= OnJumpPressed;
            _jumpAction.Disable();
        }
    }

    /// <summary>
    /// Rayで押せる家具を検出し、UI表示や処理更新を行う。
    /// </summary>
    private void Update()
    {

        // ジャンプ中ならジャンプ処理を更新して終了
        if (_isJumping)
        {
            UpdateJump();
            return;
        }

        if (_isPushing && _pushingTransform != null)
        {
            _pushingTransform.position += _pushDirection * _pushSpeed * Time.deltaTime;
        }

        // Rayで正面のオブジェクトをチェック
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin))
        {
            if (hit.collider.CompareTag("Chair"))
            {
                _pushTextMeshPro.SetText("Qキーで椅子に乗る\nFキーで椅子を押す");
                TryUpdatePush(hit);
            }
            else
            {
                // 何もヒットしていなければ解除
                _pushingRb = null;
                _pushTextMeshPro.SetText("");
            }
        }
        else
        {
            _pushingRb = null;
            _pushTextMeshPro.SetText("");
        }
    }

    /// <summary>
    /// 椅子にRayが当たっていれば押し始める。
    /// </summary>
    private void OnPushPressed(InputAction.CallbackContext context)
    {
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _pushDistance, _LayerPositoin))
        {
            if (hit.collider.CompareTag("Chair") && hit.rigidbody != null)
            {
                _pushingRb = hit.rigidbody;
                _pushingTransform = hit.collider.transform;

                _originalKinematic = _pushingRb.isKinematic;  
                _pushingRb.isKinematic = false;             

                _pushDirection = _rayOrigin.forward.normalized;
                _isPushing = true;
            }
        }
    }

    private void OnPushReleased(InputAction.CallbackContext context)
    {
        if (_pushingRb != null)
        {
            _pushingRb.isKinematic = _originalKinematic;
        }

        _pushingRb = null;
        _pushingTransform = null;
        _isPushing = false;
    }

    /// <summary>
    /// 「乗る」アクションが押された瞬間の処理 + 正面の椅子にジャンプする。
    /// </summary>
    private void OnJumpPressed(InputAction.CallbackContext context)
    {
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _pushDistance, _LayerPositoin))
        {
            if (hit.collider.CompareTag("Chair"))
            {
                Collider col = hit.collider;
                if (col == null) return;

                Vector3 topCenter = col.bounds.center + Vector3.up * col.bounds.extents.y;
                _jumpStart = _player.position;
                _jumpEnd = topCenter + Vector3.up * 0.25f;

                _jumpElapsed = 0f;
                _isJumping = true;
            }
        }
    }

    private void TryUpdatePush(RaycastHit hit)
    {
        // 押してる対象が変わったら解除
        if (_pushingRb != null && hit.rigidbody != _pushingRb)
        {
            _pushingRb = null;
        }
    }

    /// <summary>
    /// 放物線ジャンプの更新処理
    /// </summary>
    private void UpdateJump()
    {
        _jumpElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_jumpElapsed / _jumpDuration); // 0→1 に正規化

        // 水平移動（Lerp）
        Vector3 horizontal = Vector3.Lerp(_jumpStart, _jumpEnd, t);

        // 垂直移動（放物線）
        float height = Mathf.Sin(t * Mathf.PI) * _jumpHeight;

        _player.position = new Vector3(horizontal.x, horizontal.y + height, horizontal.z);

        // 着地したら終了
        if (t >= 1f)
        {
            _player.position = _jumpEnd;
            _isJumping = false;
        }
    }

    private void OnDrawGizmosSelected() // シーンでRayを可視化
    {
        if (_rayOrigin != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _pushDistance);
        }
    }
}
