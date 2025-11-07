using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// プレイヤーの周囲にあるアイテムを一定間隔で探索し、アイテムを拾う処理
/// </summary>
public class ItemPickupController2 : MonoBehaviour
{
    [Header("Item Pickup Settings")]
    [SerializeField] private float _itemPickupRange = 1.5f; // アイテム拾取距離
    [SerializeField] private string _itemTag = "Item";      // アイテムのタグ
    [SerializeField] private string _panelTag = "ItemPanel"; // 探索条件のパネル
    [SerializeField] private float _checkInterval = 0.3f;   // 探索間隔
    [SerializeField] private float _rayDistance = 1f;       // パネルを検知するレイ距離

    [Header("References")]
    [SerializeField] private Camera _camera;                // プレイヤーのカメラ
    [SerializeField] private Transform _player;             // プレイヤー位置
    [SerializeField] private TextMeshProUGUI _itemTextMeshPro;
    [SerializeField] private ItemDetailUManager _itemDetailUIManager;

    private List<GameObject> _inventory = new List<GameObject>();
    private GameObject _nearestItem;
    private Coroutine _checkRoutine;


    // Update is called once per frame
    void Update()
    {
        // プレイヤーの視線にレイを飛ばして、特定のパネルを見ているか確認
        bool lookingAtPanel = CheckLookingAtPanel();

        if (lookingAtPanel)
        {
            // 見ている間は探索ループを起動
            if (_checkRoutine == null)
                _checkRoutine = StartCoroutine(CheckItemsRoutine());
        }
        else
        {
            // 見ていない間は探索を止める
            if (_checkRoutine != null)
            {
                StopCoroutine(_checkRoutine);
                _checkRoutine = null;
                _nearestItem = null;
                _itemTextMeshPro.SetText("");
            }
        }

        // 拾う処理（Rキー）
        if (_nearestItem != null && Input.GetKeyDown(KeyCode.R))
        {
            _inventory.Add(_nearestItem);
            if (_itemDetailUIManager != null)
                _itemDetailUIManager.ToggleItem(_nearestItem);

            Destroy(_nearestItem);
            _nearestItem = null;
            _itemTextMeshPro.SetText("");
        }

        // UI更新
        if (_nearestItem != null)
            _itemTextMeshPro.SetText("Rキーでアイテムを取る");
        else
            _itemTextMeshPro.SetText("");
    }

    /// <summary>
    /// プレイヤーが特定のパネルを見ているかチェック
    /// </summary>
    private bool CheckLookingAtPanel()
    {
        Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance))
        {
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
