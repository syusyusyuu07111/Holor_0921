using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 椅子を「押す」＋「乗る（アニメ主導で上昇）」を管理するスクリプト
/// 旧：放物線ジャンプはフォールバックとして残し、PlayerControllerが未設定時のみ使用
/// </summary>
public class PushController : MonoBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private float _pushDistance = 0.2f;     // 家具を検出する距離
    [SerializeField] private float _pushSpeed = 1.5f;        // 椅子を押す速度
    [SerializeField] private LayerMask _LayerPositoin;       // 押せるオブジェクトのレイヤー

    [Header("References")]
    [SerializeField] private Transform _rayOrigin;           // レイ起点（カメラ前等）
    [SerializeField] private Transform _player;              // プレイヤーTransform（フォールバック用）
    [SerializeField] private PlayerController _playerController; // ★アニメ主導で乗るために参照

    [Header("Jump (Fallback)")]
    [SerializeField] private float _jumpDuration = 0.7f;     // 放物線：時間
    [SerializeField] private float _jumpHeight = 0.75f;      // 放物線：高さ

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI _pushTextMeshPro;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference _pushActionRef;  // 左クリック等
    [SerializeField] private InputActionReference _jumpActionRef;  // 乗るボタン

    // ランタイム
    private InputAction _pushAction;
    private InputAction _jumpAction;

    private Rigidbody _pushingRb = null;
    private Transform _pushingTransform = null;
    private Vector3 _pushDirection;
    private bool _isPushing = false;
    private bool _originalKinematic;

    // 旧・放物線ジャンプ用
    private bool _isJumping = false;
    private Vector3 _jumpStart;
    private Vector3 _jumpEnd;
    private float _jumpElapsed = 0f;

    // アニメ主導の椅子登り中フラグ（この間は放物線ジャンプを無効化）
    private bool _animDrivenClimb = false;

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

        // 押し状態の後片付け
        if (_pushingRb != null)
        {
            _pushingRb.isKinematic = _originalKinematic;
            _pushingRb = null;
        }
        _pushingTransform = null;
        _isPushing = false;
    }

    private void Update()
    {
        // アニメ主導の登り中は放物線更新を止める
        if (_isJumping && !_animDrivenClimb)
        {
            UpdateJump();
            return;
        }

        // 椅子を押す処理
        if (_isPushing && _pushingTransform != null)
        {
            _pushingTransform.position += _pushDirection * _pushSpeed * Time.deltaTime;
        }

        // レイで前方チェック＆UI
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _pushDistance, _LayerPositoin))
        {
            if (hit.collider.CompareTag("Chair"))
            {
                _pushTextMeshPro?.SetText("左クリックで押す / 乗る");
                TryUpdatePush(hit);
            }
            else
            {
                _pushingRb = null;
                _pushTextMeshPro?.SetText("");
            }
        }
        else
        {
            _pushingRb = null;
            _pushTextMeshPro?.SetText("");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 押す入力
    // ─────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────
    // 乗る入力
    // ─────────────────────────────────────────────────────────────
    private void OnJumpPressed(InputAction.CallbackContext context)
    {
        // まずはアニメ主導（PlayerControllerがある場合）
        if (_playerController != null)
        {
            Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, _pushDistance, _LayerPositoin))
            {
                if (hit.collider.CompareTag("Chair"))
                {
                    Collider col = hit.collider;
                    if (col == null) return;

                    // 椅子の天面Y（少し上に余裕）
                    Vector3 topCenter = col.bounds.center + Vector3.up * col.bounds.extents.y;
                    float targetTopY = topCenter.y + 0.25f;

                    // ★アニメ主導の登り開始
                    _playerController.BeginChairClimb(targetTopY);

                    // このスクリプト側の状態
                    _animDrivenClimb = true;
                    _isJumping = false; // 旧放物線は使わない

                    // 押していたら解除
                    OnPushReleased(default);
                    return;
                }
            }
        }

        // フォールバック：PlayerController 未設定 → 旧放物線ジャンプを実行
        Ray ray2 = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (Physics.Raycast(ray2, out RaycastHit hit2, _pushDistance, _LayerPositoin))
        {
            if (hit2.collider.CompareTag("Chair"))
            {
                Collider col = hit2.collider;
                if (col == null) return;

                Vector3 topCenter = col.bounds.center + Vector3.up * col.bounds.extents.y;
                _jumpStart = _player.position;
                _jumpEnd = topCenter + Vector3.up * 0.25f;

                _jumpElapsed = 0f;
                _isJumping = true;
                _animDrivenClimb = false;
            }
        }
    }

    private void TryUpdatePush(RaycastHit hit)
    {
        if (_pushingRb != null && hit.rigidbody != _pushingRb)
        {
            _pushingRb = null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 旧：放物線ジャンプ
    // ─────────────────────────────────────────────────────────────
    private void UpdateJump()
    {
        _jumpElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_jumpElapsed / _jumpDuration); // 0→1

        // 水平：線形
        Vector3 horizontal = Vector3.Lerp(_jumpStart, _jumpEnd, t);

        // 垂直：正弦カーブで山を作る
        float height = Mathf.Sin(t * Mathf.PI) * _jumpHeight;

        _player.position = new Vector3(horizontal.x, horizontal.y + height, horizontal.z);

        if (t >= 1f)
        {
            _player.position = _jumpEnd;
            _isJumping = false;
        }
    }

    /// <summary>
    /// アニメーションイベントから呼ぶ用：登りアニメが終わったらフラグを落とす
    /// （PlayerController は自分で EndChairClimb する設計）
    /// </summary>
    public void NotifyClimbFinished()
    {
        _animDrivenClimb = false;
        _pushTextMeshPro?.SetText("");
    }

    private void OnDrawGizmosSelected()
    {
        if (_rayOrigin != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _pushDistance);
        }
    }
}
