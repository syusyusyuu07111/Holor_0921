using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class StretchToFullScreen : MonoBehaviour
{
    private RectTransform _rt;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
        Stretch();
    }

    private void OnEnable()
    {
        Stretch();
    }

    // 画面サイズやCanvasのスケールが変わったときに呼ばれる
    private void OnRectTransformDimensionsChange()
    {
        if (!isActiveAndEnabled) return;

        if (_rt == null)
            _rt = GetComponent<RectTransform>();

        Stretch();
    }

    private void Stretch()
    {
        // 親全体にフィットさせる
        _rt.anchorMin = Vector2.zero;    // 左下
        _rt.anchorMax = Vector2.one;     // 右上
        _rt.offsetMin = Vector2.zero;    // 左下オフセットなし
        _rt.offsetMax = Vector2.zero;    // 右上オフセットなし
        _rt.pivot = new Vector2(0.5f, 0.5f);
        _rt.anchoredPosition = Vector2.zero;
    }
}
