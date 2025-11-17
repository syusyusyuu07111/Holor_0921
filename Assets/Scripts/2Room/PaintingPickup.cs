using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public enum PaintingType
{
    None,
    PaintingA,
    PaintingB
}

public class PaintingPickup : MonoBehaviour
{
    [Header("このPickupが担当する親の Painting（落ちる側）")]
    [SerializeField] private Painting _painting;   // Rigidbody & Painting が付いている絵のオブジェクト

    [Header("この絵はどの種類か（A/B/ハズレ）")]
    [SerializeField] private PaintingType _paintingType = PaintingType.None;

    [Header("プレイヤーの Transform")]
    [SerializeField] private Transform _playerTransform;

    [Header("拾える距離（この距離以内なら表示＆拾える）")]
    [SerializeField] private float _pickupDistance = 2.0f;

    [Header("落ちてから拾えるまでの最低待ち時間（秒） 0なら即OK")]
    [SerializeField] private float _minPickupDelayFromDrop = 0.0f;

    [Header("拾うときに表示する TMP テキスト")]
    [SerializeField] private TextMeshProUGUI _pickupText;

    [Header("パズル全体のフラグ管理")]
    [SerializeField] private PaintingPuzzleManager _puzzleManager;

    [Header("デバッグログを出すか")]
    [SerializeField] private bool _debugLog = true;

    private bool _isPickedUp = false;      // 拾ったかどうか

    private InputSystem_Actions _input;

    private void Awake()
    {
        if (_pickupText != null)
        {
            _pickupText.gameObject.SetActive(false);
        }
        else if (_debugLog)
        {
            Debug.LogWarning($"[{name}] _pickupText が設定されていません。テキストは表示されません。");
        }

        if (_painting == null && _debugLog)
        {
            Debug.LogError($"[{name}] _painting が設定されていません。親の Painting をインスペクタで割り当ててください。");
        }

        if (_playerTransform == null && _debugLog)
        {
            Debug.LogWarning($"[{name}] _playerTransform が設定されていません。距離判定ができません。");
        }

        _input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        if (_input == null)
        {
            _input = new InputSystem_Actions();
        }
        _input.Player.Enable();
    }

    private void OnDisable()
    {
        if (_input != null)
        {
            _input.Player.Disable();
        }
    }

    private void Update()
    {
        if (_isPickedUp) return;
        if (_playerTransform == null) return;
        if (_painting == null) return;

        // まだ落ちてない間は何もしない（テキストも出さない）
        if (!_painting.IsDropped)
        {
            if (_debugLog)
            {
                Debug.Log($"[{name}] まだ落ちていません。IsDropped={_painting.IsDropped}");
            }

            SetTextVisible(false);
            return;
        }

        // 落ちてからの経過時間（同じ入力で即拾われるのが嫌ならここで少し待つ）
        float elapsedFromDrop = Time.time - _painting.DroppedTime;

        // プレイヤーとの距離（高さは無視）
        Vector3 p = _playerTransform.position;
        Vector3 e = transform.position;
        p.y = 0f;
        e.y = 0f;

        float distance = Vector3.Distance(e, p);
        bool inRange = distance <= _pickupDistance;

        // ★ 表示条件：落ちている ＋ 距離内
        SetTextVisible(inRange);

        if (_debugLog)
        {
            Debug.Log($"[{name}] inRange={inRange} 距離={distance:F2}, elapsedFromDrop={elapsedFromDrop:F2}");
        }

        // ★ 拾える条件：
        // ・落ちている（ここまで来てる時点でIsDropped=true）
        // ・距離内
        // ・落ちてから_minPickupDelayFromDrop秒以上経過
        bool canPickup =
            inRange &&
            elapsedFromDrop >= _minPickupDelayFromDrop;

        if (canPickup && _input.Player.Interact.WasPressedThisFrame())
        {
            if (_debugLog)
            {
                Debug.Log($"[{name}] Interact 入力検知 → Pickup() 実行");
            }

            Pickup();
        }
    }

    private void SetTextVisible(bool visible)
    {
        if (_pickupText == null)
        {
            if (_debugLog)
            {
                Debug.LogWarning($"[{name}] SetTextVisible({visible}) したいけど _pickupText が null です。");
            }
            return;
        }

        _pickupText.gameObject.SetActive(visible);

        if (_debugLog)
        {
            Debug.Log($"[{name}] SetTextVisible({visible}) → textObj={_pickupText.gameObject.name}, activeSelf={_pickupText.gameObject.activeSelf}");
        }
    }

    private void Pickup()
    {
        _isPickedUp = true;

        // パズル用フラグON
        if (_puzzleManager != null)
        {
            switch (_paintingType)
            {
                case PaintingType.PaintingA:
                    _puzzleManager.pickedUpPaintingA = true;
                    break;
                case PaintingType.PaintingB:
                    _puzzleManager.pickedUpPaintingB = true;
                    break;
            }
        }

        // 拾ったらテキストを消す
        SetTextVisible(false);

        if (_debugLog)
        {
            Debug.Log($"[{name}] Pickup 完了。絵を非表示にします。");
        }

        if (_painting != null)
        {
            _painting.gameObject.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
