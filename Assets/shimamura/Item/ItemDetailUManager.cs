using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemDetailUManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject _panel;          // 詳細ウィンドウ全体
    [SerializeField] private Image _icon;                // アイコン画像
    [SerializeField] private TMP_Text _nameText;         // アイテム名
    [SerializeField] private TMP_Text _explanatoryText;  // アイテム説明

    private bool _isOpen = false;                        // 現在ウィンドウが開いているか

    public bool IsOpen => _isOpen;                       //外部から読み取れるように


    /// <summary>
    /// アイテム情報をトグル表示（開いてなければ開く、開いてたら閉じる）
    /// </summary>
    public void ToggleItem(GameObject itemObject)
    {
        if (_isOpen)
        {
            // すでに開いていたら閉じる
            HideWindow();
        }
        else
        {
            // 閉じていたら表示する
            ShowItem(itemObject);
        }
    }

    /// <summary>
    /// アイテム情報を表示
    /// </summary>
    public void ShowItem(GameObject itemObject)
    {
        if (itemObject == null) return;

        var data = itemObject.GetComponent<ItemDate>();
        if (data == null)
        {
            Debug.LogWarning($"ItemData が {itemObject.name} に見つかりません。");
            return;
        }

        // --- UIに反映 ---
        if (_icon != null) _icon.sprite = data.icon;//アイコン画像の表示
        if (_nameText != null) _nameText.text = data.itemName;//アイテム名
        if (_explanatoryText != null) _explanatoryText.text = data.explanatoryText;//アイテムテキスト

        // --- パネル表示 ---
        _panel.SetActive(true);
        _isOpen = true;
        Debug.Log(_isOpen);

        //タイムスケール停止
        // Time.timeScale = 0;
    }

    /// <summary>
    /// ウィンドウを閉じる
    /// </summary>
    public void HideWindow()
    {
        Debug.Log("HideWindow");
        if (_panel != null)
            _panel.SetActive(false);

        //Time.timeScale = 1.0f;//タイムスケール再開

        _isOpen = false;
    }
}
