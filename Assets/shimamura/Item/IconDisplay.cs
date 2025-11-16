using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class IconDisplay : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private RectTransform _iconParent;  // アイコンを配置する親（右上のUI）
    [SerializeField] private GameObject _iconPrefab;     // 表示用のアイコンPrefab（Imageが含まれている）
    [SerializeField] private float _iconSpacing = 70f;   // アイコン間の間隔（ピクセル）

    private readonly List<GameObject> _spawnedIcons = new(); // 表示中のアイコン一覧
    private readonly Dictionary<string, GameObject> _iconMap = new();


    // <summary>
    // アイテム取得時に呼び出してアイコンを追加
    // </summary>
    public void AddItemIcon(ItemDate itemDate)
    {
        if (itemDate == null || _iconParent == null || _iconPrefab == null)
            return;

        string key = itemDate.itemNameOrigin;

        if (_iconMap.TryGetValue(key, out GameObject existingIcon))
        {
            // 既存アイコンの個数を増やす
            var countText = existingIcon.transform.Find("CountText")?.GetComponent<TextMeshProUGUI>();
            if (countText != null)
            {
                if (int.TryParse(countText.text, out int count))
                {
                    countText.text = (count + 1).ToString();
                }
            }
            return;
        }

        // 新しいアイコンを生成
        GameObject newIcon = Instantiate(_iconPrefab, _iconParent);
        Debug.Log(newIcon.name);

        // アイコン画像設定
        var image = newIcon.transform.Find("IconImage")?.GetComponent<Image>();
        if (image != null) image.sprite = itemDate.icon;

        // 個数表示初期化
        var countTextInit = newIcon.GetComponentInChildren<TextMeshProUGUI>();
        if (countTextInit != null)
            countTextInit.text = "1";

        // 管理リストに追加
        _spawnedIcons.Add(newIcon);
        _iconMap[key] = newIcon;

        // アイコン位置を並べ直す
        UpdateIconPositions();
    
}

    /// <summary>
    /// アイコンを並べ直す（右→左に）
    /// </summary>
    private void UpdateIconPositions()
    {
        for (int i = 0; i < _spawnedIcons.Count; i++)
        {
            RectTransform rt = _spawnedIcons[i].GetComponent<RectTransform>();
            if (rt == null) continue;

            // 右上基準で、左方向にずらして配置
            rt.anchoredPosition = new Vector2(-_iconSpacing * i, 0);
        }
    }
}
