using TMPro;
using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.UI;

public class ItemDetailUManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject _panel;          // 詳細ウィンドウ全体
    [SerializeField] private Image _icon;                // アイコン画像
    [SerializeField] private TMP_Text _nameText;         // アイテム名
    [SerializeField] private TMP_Text _explanatoryText;  // アイテム説明
    [SerializeField] private TMP_Text _exitText;         //閉じるUI


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

        ItemDate data = itemObject.GetComponent<ItemDate>();
        if (data == null) return;

        if (_icon != null) _icon.sprite = data.icon;
        if (_nameText != null) _nameText.text = data.itemName;
        if (_explanatoryText != null) _explanatoryText.text = data.explanatoryText;

        _panel.SetActive(true);
        _isOpen = true;


    }

    /// <summary>
    /// ウィンドウを閉じる
    /// </summary>
    public void HideWindow()
    {
        if (_panel != null)
            _panel.SetActive(false);

        _isOpen = false;
    }
}
