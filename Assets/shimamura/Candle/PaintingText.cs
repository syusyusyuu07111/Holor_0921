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
            if (_targetTags.Contains(hit.collider.tag))
            {
                _text.text = _hitMessage;
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
