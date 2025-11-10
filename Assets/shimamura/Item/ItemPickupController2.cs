using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーの周囲にあるアイテムを一定間隔で探索し、アイテムを拾う処理
/// </summary>
public class ItemPickupController2 : MonoBehaviour
{
    [Header("Item Pickup Settings")]
    [SerializeField] private float _itemPickupRange = 1.5f; // アイテム拾取距離
    [SerializeField] private string _itemTag = "Item";      // アイテムのタグ
    [SerializeField] private string _panelTag = "ItemPanel"; // 探索条件のパネル
    [SerializeField] private float _checkInterval = 0.5f;   // 探索間隔
    [SerializeField] private float _rayDistance = 1f;       // パネルを検知するレイ距離
    [SerializeField] private LayerMask _panelLayer;         //パネル用レイヤー
    [SerializeField] private IconDisplay _iconDisplay;

    [Header("References")]
    [SerializeField] private Transform _rayOrigin;                // プレイヤーのカメラ
    [SerializeField] private Transform _player;             // プレイヤー位置
    [SerializeField] private TextMeshProUGUI _itemTextMeshPro;
    [SerializeField] private ItemDetailUManager _itemDetailUI;

    private List<GameObject> _inventory = new List<GameObject>();
    private GameObject _nearestItem;
    private Coroutine _checkRoutine;
    private bool _isLookingAtPanel;

    [Header("Input System")]
    [SerializeField] private InputActionReference _pickupActionRef;
    [SerializeField] private InputActionReference _cancelActionRef;
    private InputAction _pickupAction;
    private InputAction _cancelAction;

    private void OnEnable()
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

    private void OnDisable()
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
        if (_isLookingAtPanel && _nearestItem != null)
        {
            PickupItem(_nearestItem);
        }
    }
    /// <summary>
    /// ESCキーが押されたときの処理
    /// </summary>
    private void OnCancelPressed(InputAction.CallbackContext context)
    {
        Debug.Log("[ItemSystem] ESCキー入力検知！");
        _itemDetailUI?.HideWindow();

        // アイテム名などテキストだけ手動でクリア
        if (_itemTextMeshPro != null)
            _itemTextMeshPro.text = "";
        // HideItemUI();
    }


    // Update is called once per frame
    void Update()
    {
        // 見ているか判定
        bool lookingNow = CheckLookingAtPanel();

        // 状態が変わったときだけコルーチンを開始・停止
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
                _itemTextMeshPro.text = "";
            }
        }
    }

    /// <summary>
    /// アイテムを取得したときの処理
    /// </summary>
    private void PickupItem(GameObject item)
    {
        _inventory.Add(item);

        // --- アイコン追加（連携用） ---
        ItemDate data = item.GetComponent<ItemDate>();
        if (data != null && _iconDisplay != null)
        {
            _iconDisplay.AddItemIcon(data);
        }

        // --- 詳細UI表示 ---
        _itemDetailUI?.ToggleItem(item);

        // --- アイテムを削除 ---
        Destroy(item);

        _nearestItem = null;
        _itemTextMeshPro.text = "";
    }

    /// <summary>
    /// プレイヤーが特定のパネルを見ているかチェック
    /// </summary>
    private bool CheckLookingAtPanel()
    {
        if (_rayOrigin == null) return false;

        Ray ray = new Ray(_rayOrigin.position, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _panelLayer))
        {
            Debug.Log($"[ItemSystem]Hit: {hit.collider.name}, Tag: {hit.collider.tag}");
            return hit.collider.CompareTag(_panelTag);
        }
        return false;
    }


    /// <summary>
    /// 一定間隔でアイテムを探索するループ
    /// </summary>
    private IEnumerator CheckItemsRoutine()
    {
        while (true)
        {
            FindNearestItem();
            yield return new WaitForSeconds(_checkInterval);
        }
    }

    /// <summary>
    /// 一番近いアイテムを探す
    /// </summary>
    private void FindNearestItem()
    {
        GameObject[] allItems = GameObject.FindGameObjectsWithTag(_itemTag);
        if (allItems.Length == 0)
        {
            _nearestItem = null;
            _itemTextMeshPro.text = ""; 
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

        if (_nearestItem != null && _itemTextMeshPro != null)
        {
            _itemTextMeshPro.text = "Qキーでアイテムを拾う";
        }
        else
        {
            _itemTextMeshPro.text = "";
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_player != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_player.position, _itemPickupRange);
        }
    }
}
