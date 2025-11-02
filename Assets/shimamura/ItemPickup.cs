using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 家具の上でアイテムを取得する処理を管理するスクリプト
/// </summary>
public class ItemPickupController : MonoBehaviour
{
    [Header("Item Pickup Settings")]
    [SerializeField] private float _itemPickupRange = 1f;       // アイテムを拾える距離
    [SerializeField] private LayerMask _itemLayer;              // アイテムのレイヤー（例：Item）

    [Header("References")]
    [SerializeField] private Transform _player;                 // プレイヤー本体
    [SerializeField] private TextMeshProUGUI _itemTextMeshPro;  // アイテム取得テキスト
    [SerializeField] private ItemDetailUManager _itemDetailUIManager; // 詳細ウィンドウ管理スクリプト

    // インベントリ
    private List<GameObject> _inventory = new List<GameObject>();  // 拾ったアイテムを保存するリスト

    private void Update()
    {
        CheckItemPickup();
    }

    /// <summary>
    /// 家具の上でアイテムを取得する処理
    /// </summary>
    private void CheckItemPickup()
    {
        Collider[] items = Physics.OverlapSphere(_player.position, _itemPickupRange, _itemLayer);

        // 該当するアイテムが1つもない場合は処理をスキップ
        if (items.Length == 0)
        {
            _itemTextMeshPro.SetText("");
            return;
        }

        _itemTextMeshPro.SetText("Rキーでアイテムを取る");

        // 一番近いアイテムを記録する変数
        Collider nearestItem = null;
        float minDist = Mathf.Infinity;

        // 取得した全アイテムを調べる
        foreach (var item in items)
        {
            // レイヤーが正しいか再確認（OverLapSphereの結果に他の物が混ざった場合用）
            if (((1 << item.gameObject.layer) & _itemLayer) == 0) continue;

            // 距離を計算
            float dist = Vector3.Distance(_player.position, item.transform.position);

            // より近ければ更新
            if (dist < minDist)
            {
                minDist = dist;
                nearestItem = item;
            }
        }

        // 一番近いアイテムがあり、Rキーを押したら拾う
        if (nearestItem != null && Input.GetKeyDown(KeyCode.R))
        {
            _inventory.Add(nearestItem.gameObject); // インベントリ追加

            if (_itemDetailUIManager != null)
            {
                _itemDetailUIManager.ToggleItem(nearestItem.gameObject);
            }

            Destroy(nearestItem.gameObject); // 必要なら削除
        }
    }

    private void OnDrawGizmosSelected() // シーンで範囲を可視化
    {
        if (_player != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_player.position, _itemPickupRange);
        }
    }
}
