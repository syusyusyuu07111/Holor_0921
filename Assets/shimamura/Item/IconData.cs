using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class IconData : MonoBehaviour
{
    [Header("UI参照")]
    public Image iconImage;            // アイコン画像
    public TextMeshProUGUI countText;  // 数字用テキスト

    /// <summary>
    /// 数字を増やす
    /// </summary>
    public void IncrementCount(int amount = 1)
    {
        if (countText == null) return;

        int count;
        if (!int.TryParse(countText.text, out count))
            count = 0;

        count += amount;
        countText.text = count.ToString();
    }

    /// <summary>
    /// 数字を初期化
    /// </summary>
    public void SetCountToOne()
    {
        if (countText != null)
            countText.text = "1";
    }
}
