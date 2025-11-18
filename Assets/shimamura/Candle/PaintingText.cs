using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PaintingText : MonoBehaviour
{
    [Header("Ray Origin")]
    [SerializeField] private Transform _rayOrigin;   // レイを飛ばすオブジェクト
    [SerializeField] private float _rayDistance = 100f; // レイの最大距離

    [Header("UI")]
    [SerializeField] private TMP_Text _text;         // 表示用TextMeshPro
    [SerializeField] private string _hitMessage = "Qキーで絵を見る";

    [Header("Target Tags")]
    [SerializeField] private List<string> _targetTags = new List<string> { "Picture", "Item" };

    // 各タグごとのメッセージを設定するリスト（_targetTags と同じ順番で並べる）
    [Header("Tag Messages (Target Tags と同じ順番で設定)")]
    [SerializeField] private List<string> _tagMessages = new List<string>();

    private void Start()
    {
        if (_text != null)
            _text.gameObject.SetActive(false);

        if (_rayOrigin == null) return;
    }

    private void Update()
    {
        if (_rayOrigin == null || _text == null) return;

        // レイの作成（オブジェクトの位置と前方向）
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance))
        {
            string hitTag = hit.collider.tag;

            if (_targetTags.Contains(hitTag))
            {
                // タグのインデックスを取得
                int index = _targetTags.IndexOf(hitTag);

                // 対応するメッセージがあればそれを使う。なければ従来の _hitMessage を使う
                string messageToShow = _hitMessage;

                if (index >= 0 && index < _tagMessages.Count)
                {
                    string msg = _tagMessages[index];
                    if (!string.IsNullOrEmpty(msg))
                    {
                        messageToShow = msg;
                    }
                }

                _text.text = messageToShow;
                _text.gameObject.SetActive(true);
            }
            else
            {
                _text.gameObject.SetActive(false);
            }
        }
        else
        {
            _text.gameObject.SetActive(false);
        }
    }
}
