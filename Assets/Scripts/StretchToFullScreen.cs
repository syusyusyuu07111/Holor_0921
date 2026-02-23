using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class StretchToFullScreen : MonoBehaviour
{
    /*
        ============================================================
        StretchToFullScreen がやること
        ============================================================

        ■目的
        ・このUI(RectTransform)を「親のRectTransformいっぱい」に広げて、
          常にフルスクリーン（親にぴったり）表示にする。

        ■いつ実行する？
        1) Awake        : 最初に一度だけ（生成直後）
        2) OnEnable     : 有効化されたタイミング
        3) OnRectTransformDimensionsChange :
           画面サイズ、Canvasのスケール、親Rectのサイズなどが変わったとき

        ■やっていること（Stretch）
        ・anchorMin = (0,0) ＝ 親の左下にアンカー
        ・anchorMax = (1,1) ＝ 親の右上にアンカー
        → アンカーを「親全体」にする

        ・offsetMin / offsetMax を (0,0) にする
        → アンカーからの余白（左右上下のはみ出し）をゼロにする
           ＝ ぴったり親にフィット

        ・pivot を中央、anchoredPosition を0にして安定させる
        ============================================================
    */

    private RectTransform _rt;

    private void Awake()
    {
        // RectTransform をキャッシュ
        _rt = GetComponent<RectTransform>();

        // 起動直後に一度フィットさせる
        Stretch();
    }

    private void OnEnable()
    {
        // 非表示→表示に戻った時も、念のため再フィット
        Stretch();
    }

    // 画面サイズ、Canvasスケール、親Rectサイズが変わったときに呼ばれる
    private void OnRectTransformDimensionsChange()
    {
        // 非アクティブ/無効中は触らない（余計な処理回避）
        if (!isActiveAndEnabled) return;

        // 念のため null のときだけ取り直し（基本 Awake で入っている）
        if (_rt == null)
            _rt = GetComponent<RectTransform>();

        // サイズが変わった＝もう一回ぴったり合わせる
        Stretch();
    }

    // 親のRectTransformにピッタリ合わせる本体
    private void Stretch()
    {
        if (_rt == null) return;

        // アンカーを「親の左下～右上」に設定（親全体）
        _rt.anchorMin = Vector2.zero; // (0,0)
        _rt.anchorMax = Vector2.one;  // (1,1)

        // アンカーからの余白をゼロにする（＝完全フィット）
        _rt.offsetMin = Vector2.zero; // 左下余白なし
        _rt.offsetMax = Vector2.zero; // 右上余白なし

        // 中心基準にして、位置ズレが起きにくいようにする
        _rt.pivot = new Vector2(0.5f, 0.5f);
        _rt.anchoredPosition = Vector2.zero;
    }
}