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
    [SerializeField] private float _pushForce = 2f;           // 押す力（小さめにしてスライド感）
    [SerializeField] private LayerMask _LayerPositoin;        // 押せるオブジェクトのレイヤー

    [Header("References")]
    [SerializeField] private Transform _rayOrigin;            // レイを飛ばす起点
    [SerializeField] private Transform _player;               // プレイヤー本体（位置を動かす対象）

    [Header("Jump Settings")]
    [SerializeField] private float _jumpDuration = 0.7f;      // 飛ぶ時間（秒）
    [SerializeField] private float _jumpHeight = 0.75f;       // 放物線の高さ

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI _pushTextMeshPro;  // 椅子を押す＋乗るテキスト

    [SerializeField] private InputActionReference _interactActionRef;
    private InputAction _interactAction;

    private Rigidbody _pushingRb = null;     // 押しているオブジェクト
    private Vector3 _pushDirection;          // 押す方向

    // ジャンプ関連
    private bool _isJumping = false;         // 飛行中フラグ
    private Vector3 _jumpStart;              // ジャンプ開始位置
    private Vector3 _jumpEnd;                // ジャンプ目標位置
    private float _jumpElapsed = 0f;         // ジャンプ経過時間

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
        
    }

    private void Update()
    {
        // ジャンプ中ならジャンプ処理を更新して終了
        if (_isJumping)
        {
            UpdateJump();
            return;
        }

        // Rayで正面のオブジェクトをチェック
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin))
        {
            _pushTextMeshPro.SetText("Qキーで椅子に乗る\nFキーで椅子を押す");
            ChairPush(hit);       // 椅子を押す処理
            HandleJumpInput(hit); // ジャンプ処理
        }
        else
        {
            // 何もヒットしていなければ解除
            _pushingRb = null;
            _pushTextMeshPro.SetText("");
        }
    }

    /// <summary>
    /// Fキーで椅子を押す処理
    /// </summary>
    private void ChairPush(RaycastHit hit)
    {
        if (Input.GetKey(KeyCode.F))
        {
            if (_pushingRb == null)
            {
                _pushingRb = hit.rigidbody;
                if (_pushingRb != null)
                {
                    _pushDirection = -hit.normal; // 押す方向をレイの逆方向に設定
                }
            }
        }
        else
        {
            _pushingRb = null; // Fキーを離したら解除
        }
    }

    /// <summary>
    /// Eキーで椅子の上に放物線ジャンプする処理
    /// </summary>
    private void HandleJumpInput(RaycastHit hit)
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            Collider col = hit.collider;
            if (col == null) return;

            // オブジェクトの上面中央をジャンプ目標に設定
            Vector3 topCenter = col.bounds.center + Vector3.up * col.bounds.extents.y;
            _jumpStart = _player.position;
            _jumpEnd = topCenter + Vector3.up * 0.05f;

            _jumpElapsed = 0f;
            _isJumping = true;
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
            _isJumping = false;
        }
    }

    private void FixedUpdate()
    {
        // 押している間、押す力をゆっくり加える
        if (_pushingRb != null)
        {
            _pushingRb.AddForce(_pushDirection * _pushForce, ForceMode.Force); // 継続的に力を加える
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
