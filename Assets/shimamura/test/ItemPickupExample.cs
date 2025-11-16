using UnityEngine;

public class ItemPickupExample : MonoBehaviour
{
    [SerializeField] private IconDisplay _iconDisplay; // アイコン表示スクリプト
    [SerializeField] private ItemDate _itemData;       // このアイテムの情報

    private bool _isCollected = false;

    private void Update()
    {
        // 例: プレイヤーが近づいて "E" を押すと拾う
        if (!_isCollected && Input.GetKeyDown(KeyCode.Y))
        {
            PickupItem();
        }
    }

    private void PickupItem()
    {
        _isCollected = true;
        _iconDisplay.AddItemIcon(_itemData); // アイコン追加
        Debug.Log($"{_itemData.itemName} を拾った！");
        Destroy(gameObject); // アイテム消滅
    }
}
