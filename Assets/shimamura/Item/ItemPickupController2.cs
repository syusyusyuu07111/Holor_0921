using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

/// <summary>
/// プレイヤー周囲のアイテム探索＆拾い処理。
/// さらに「椅子に上れるタイミング」だけを距離で検出してイベント通知する。
/// ※ここではプレイヤー/カメラ/椅子を動かさない（検出とUIだけ）
/// </summary>
public class ItemPickupController2 : MonoBehaviour
{
    [Header("Item Pickup Settings")]
    [SerializeField] private float _defaultPickupRange; //アイテムの取得範囲
    [SerializeField] private float _itemPickupRange = 1.5f; // アイテム拾取距離（地面）
    [SerializeField] private float _itemPickupRangeOnChair = 2f;
    [SerializeField] private string _itemTag = "Item";      // アイテムのタグ
    [SerializeField] private string _panelTag = "ItemPanel"; // 探索条件のパネル
    [SerializeField] private float _checkInterval = 0.5f;   // 探索間隔
    [SerializeField] private float _rayDistance = 1f;       // パネルを検知するレイ距離
    [SerializeField] private LayerMask _panelLayer;         // パネル用レイヤー
    [SerializeField] private IconDisplay _iconDisplay;
    [SerializeField] private Transform _upRayOrigin;
    private bool _isOnChair = false;   // 椅子上にいる間は true


    [Header("Chair Climb Timing (Detect Only)")]
    [SerializeField] private string _chairTag = "Chair";    // 椅子タグ PickupItemByChairでも使用
    [SerializeField] private float _chairCheckRange = 0.9f; // 「上れる」と見なす距離
    [Tooltip("上れるタイミングを見つけたら発火（ここでは移動しない）。Args: (chairTransform, chairTopY)")]
    public UnityEvent<Transform, float> OnChairClimbRequested;

    [Header("References")]
    [SerializeField] private Transform _rayOrigin;          // 視線や足元判定の基点（読み取りのみ）
    [SerializeField] private Transform _player;             // プレイヤー位置（距離計算のみ）
    [SerializeField] private GameObject _itemText;          // アイテムUI
    [SerializeField] private ItemDetailUManager _itemDetailUI;

    private readonly List<GameObject> _inventory = new List<GameObject>();
    private GameObject _nearestItem;
    private Coroutine _checkRoutine;
    private bool _isLookingAtPanel;

    //いままでに取ったアイテムの数//取得したアイテムの合計数取得したかったので追加しました。
    public int CollectedCount { get; private set; }

    [Header("Input System")]
    [SerializeField] private InputActionReference _pickupActionRef; // 既存：アイテム拾い
    [SerializeField] private InputActionReference _cancelActionRef; // 既存：UI閉じ
    private InputAction _pickupAction;
    private InputAction _cancelAction;

    void OnEnable()
    {
        if (_pickupActionRef != null)
        {
            _pickupAction = _pickupActionRef.action;
            _pickupAction.Enable();
            _pickupAction.performed += OnPickupPressed;
        }
        if (_cancelActionRef != null)
        {
            _cancelAction = _cancelActionRef.action;
            _cancelAction.Enable();
            _cancelAction.performed += OnCancelPressed;
        }
    }

    void OnDisable()
    {
        if (_pickupAction != null)
        {
            _pickupAction.performed -= OnPickupPressed;
            _pickupAction.Disable();
        }
        if (_cancelAction != null)
        {
            _cancelAction.performed -= OnCancelPressed;
            _cancelAction.Disable();
        }
    }

    private void OnPickupPressed(InputAction.CallbackContext context)
    {
        // アイテム拾いだけをここで実行（移動はしない）
        if (_isLookingAtPanel && _nearestItem != null)
        {
            PickupItem(_nearestItem);
        }
    }

    /// <summary>ESCキーなどで詳細UIを閉じる</summary>
    private void OnCancelPressed(InputAction.CallbackContext context)
    {
        _itemDetailUI?.HideWindow();
        if (_itemText != null) _itemText.SetActive(false);
    }

    private void Start()
    {
        _defaultPickupRange = _itemPickupRange;
    }

    void Update()
    {
        // パネルを見ているか（足元にパネルを想定）
        bool lookingNow = _isOnChair || CheckLookingAtPanel();

        // 状態変化でコルーチン起動/停止
        if (lookingNow != _isLookingAtPanel)
        {
            _isLookingAtPanel = lookingNow;
            if (_isLookingAtPanel)
            {
                _checkRoutine ??= StartCoroutine(CheckItemsRoutine());
            }
            else
            {
                if (_checkRoutine != null)
                {
                    StopCoroutine(_checkRoutine);
                    _checkRoutine = null;
                }
                _nearestItem = null;
                if (_itemText) _itemText.SetActive(false);
            }
        }
        PickupItemByChair();
        // ここでは「上れるタイミング」を距離だけで検出し、イベント通知だけ行う
        DetectChairClimbTiming();
    }

    // ====== 椅子：距離だけで「今上れる」を検出してイベント通知 ======
    private void DetectChairClimbTiming()
    {
        if (!_isLookingAtPanel || _player == null) return;

        // 近い椅子を探索（距離のみ）
        GameObject[] chairs = GameObject.FindGameObjectsWithTag(_chairTag);
        if (chairs == null || chairs.Length == 0) return;

        Transform nearestChair = null;
        float minDist = float.PositiveInfinity;
        float topY = 0f;

        foreach (var c in chairs)
        {
            if (!c) continue;
            float d = Vector3.Distance(_player.position, c.transform.position);
            if (d < _chairCheckRange && d < minDist)
            {
                minDist = d;
                nearestChair = c.transform;

                // 椅子の天面Yをざっくり計算（Colliderがあればそれを利用）
                float y = c.transform.position.y;
                var col = c.GetComponent<Collider>();
                if (col != null)
                {
                    y = col.bounds.center.y + col.bounds.extents.y;
                }
                topY = y;
            }
        }

        // 条件を満たしていれば「今上れる」→ イベントを発火（移動は呼び先に任せる）
        if (nearestChair != null && OnChairClimbRequested != null)
        {
            OnChairClimbRequested.Invoke(nearestChair, topY);
            // ※連発させたくない場合は、呼び先で一度だけ受ける/自身でクールダウン等を実装してください
        }
    }

    // ====== アイテム関連（既存のまま） ======

    /// <summary>一定間隔でアイテムを探索するループ</summary>
    private IEnumerator CheckItemsRoutine()
    {
        while (true)
        {
            FindNearestItem();
            yield return new WaitForSeconds(_checkInterval);
        }
    }

    /// <summary>一番近いアイテムを探す</summary>
    private void FindNearestItem()
    {
        GameObject[] allItems = GameObject.FindGameObjectsWithTag(_itemTag);
        if (allItems.Length == 0)
        {
            _nearestItem = null;
            if (_itemText) _itemText.SetActive(false);
            return;
        }

        float minDist = Mathf.Infinity;
        GameObject nearest = null;

        foreach (var item in allItems)
        {
            float dist = Vector3.Distance(_player.position, item.transform.position);
            if (dist < _itemPickupRange && dist < minDist)
            {
                minDist = dist;
                nearest = item;
            }
        }

        _nearestItem = nearest;

        if (_nearestItem != null && _itemText != null)
        {
            _itemText.SetActive(true);
        }
        else
        {
            if (_itemText) _itemText.SetActive(false);
        }
    }

    /// <summary>アイテムを取得</summary>
    private void PickupItem(GameObject item)
    {
        _inventory.Add(item);

        // 取得したアイテム数をここでカウントを1つ増やす
        CollectedCount++;

        // アイコン連携
        ItemDate data = item.GetComponent<ItemDate>();
        if (data != null && _iconDisplay != null)
        {
            _iconDisplay.AddItemIcon(data);
        }

        // 詳細UI
        _itemDetailUI?.ToggleItem(item);

        // アイテム破棄
        Destroy(item);

        _nearestItem = null;
        if (_itemText) _itemText.SetActive(false);
    }

    private void PickupItemByChair()
    {
        if (_rayOrigin == null) return;

        Ray ray = new Ray(_upRayOrigin.position, Vector3.down);

        if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _panelLayer))
        {
            Debug.Log("Ray hit tag: " + hit.collider.tag);
            // 足元が椅子なら拾取範囲を広げる
            if (hit.collider.CompareTag(_chairTag))
            {
                _itemPickupRange = _itemPickupRangeOnChair; // ← 好きな距離に変更！
                _isOnChair = true;
                return;
            }
            else
            {
                // パネルには当たったけど椅子ではない（例：ItemPanel）
                _itemPickupRange = _defaultPickupRange;
                _isOnChair = false;
                return;
            }
        }

        // 椅子じゃない → 元に戻す
        _itemPickupRange = _defaultPickupRange;
        _isOnChair = false;
    }

    /// <summary>プレイヤーが特定のパネルを見ているかチェック</summary>
    private bool CheckLookingAtPanel()
    {
        if (_rayOrigin == null) return false;

        Ray ray = new Ray(_rayOrigin.position, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _panelLayer))
        {
            return hit.collider.CompareTag(_panelTag);
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (_player != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_player.position, _itemPickupRange);
        }

        // 椅子タイミング用の距離参考
        if (_player != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_player.position, _chairCheckRange);
        }
    }
}
