using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class IconDisplay : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private RectTransform _iconParent;  // アイコンを配置する親（右上のUI）
    [SerializeField] private GameObject _iconPrefab;     // 表示用のアイコンPrefab（Imageが含まれている）
    [SerializeField] private float _iconSpacing = 70f;   // アイコン間の間隔（ピクセル）

    private readonly List<GameObject> _spawnedIcons = new(); // 表示中のアイコン一覧

    /// <summary>
    /// アイテム取得時に呼び出してアイコンを追加
    /// </summary>
    public void AddItemIcon(ItemDate itemDate)
    {
        if (itemDate == null)
        {
            return;
        }
        if (_iconParent == null || _iconPrefab == null)
        {
            return;
        }

        // --- 新しいアイコンを生成 ---
        GameObject newIcon = Instantiate(_iconPrefab, _iconParent);
        // --- イメージを設定 ---
        var image = newIcon.GetComponent<Image>();
        if (image != null)
            image.sprite = itemDate.icon;

        _spawnedIcons.Add(newIcon);

        // --- 位置調整 ---
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
