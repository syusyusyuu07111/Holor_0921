using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class GameOverSequence : MonoBehaviour
{
    public enum RevealMode
    {
        FadeIn,             // ただのフェード(αだけ上げる)
        LeftToRight,        // 左→右に露出
        RightToLeft,        // 右→左に露出
        TopToBottom,        // 上→下に露出
        BottomToTop,        // 下→上に露出
        DiagonalTLBR        // 左上→右下っぽく露出（横+縦同時）
    }

    [System.Serializable]
    public class ShowStep
    {
        [Header("このタイミングで表示するUI(画像そのままでOK)")]
        public GameObject target;          // 画像そのものを入れていい。親は自動で作る

        [Header("このステップの前に待つ秒数")]
        public float delayBeforeShow = 0.5f;

        [Header("演出タイプ")]
        public RevealMode mode = RevealMode.FadeIn;

        [Header("演出にかける時間(秒)")]
        public float duration = 0.4f;

        // ランタイム用：実際にアニメさせる先(= RectMask2D付きのwrapper or 自分自身)
        [System.NonSerialized] public GameObject runtimeTarget;
    }

    [Header("順番に表示するリスト(上から順に再生)")]
    public ShowStep[] steps;

    [Header("最初に全部隠す")]
    public bool hideAllOnStart = true;

    [Header("Start時に自動再生")]
    public bool playOnStart = true;

    void Start()
    {
        // 1) 必要ならターゲットを自動ラップして、runtimeTarget を確定させる
        PrepareRuntimeTargets();

        // 2) 最初ぜんぶ隠す
        if (hideAllOnStart)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                HideInstant(steps[i]);
            }
        }

        // 3) 自動再生
        if (playOnStart)
        {
            StartCoroutine(PlaySequence());
        }
    }

    public void PlayNow()
    {
        StartCoroutine(PlaySequence());
    }

    // ============================================================
    // ランタイムで「ターゲットをマスク対応のWrapperに包む」
    // ============================================================
    private void PrepareRuntimeTargets()
    {
        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            if (!s.target)
            {
                s.runtimeTarget = null;
                continue;
            }

            // もしこのステップが FadeIn なら、ラップ不要でも動くのでそのままでもOK
            // ただし、後で一律にCanvasGroupいじるから、最終的にruntimeTargetに何が入るかは揃えたい

            // すでにRectMask2Dを持ってる親がいるなら、それをruntimeTargetに使う
            // （手動でWrapper作ってある場合もちゃんと動く）
            if (HasRectMaskSelfOrParent(s.target, out GameObject maskRoot))
            {
                s.runtimeTarget = maskRoot;
                continue;
            }

            // ここからが「自動ラップ」：
            // - targetのRectTransform情報を拾う
            // - 同じ場所・サイズの空オブジェクト(wrapper)を作る
            // - wrapperにRectMask2DとCanvasGroupを付ける
            // - targetをwrapperの子にする
            // - steps[i].runtimeTarget = wrapper にする
            var rt = s.target.GetComponent<RectTransform>();
            if (!rt)
            {
                // UIじゃない(たとえば普通の3D obj)ならラップせずそのまま扱う
                s.runtimeTarget = s.target;
                continue;
            }

            // wrapper作成
            GameObject wrapper = new GameObject(s.target.name + "_Wrapper");
            var wrapperRT = wrapper.AddComponent<RectTransform>();
            wrapperRT.SetParent(rt.parent, worldPositionStays: false);

            // 元のRectTransformの見た目(アンカー/ピボット/pos/size/scale/rotation)をコピー
            CopyRectTransform(rt, wrapperRT);

            // wrapper側にRectMask2DとCanvasGroupを追加
            var mask = wrapper.AddComponent<RectMask2D>();
            var cg = wrapper.AddComponent<CanvasGroup>();
            cg.alpha = 1f; // 初期値はとりあえず1にしておく、あとでHideInstantで0にする

            // 子に付け替え
            rt.SetParent(wrapperRT, worldPositionStays: false);

            // runtimeTargetはこのwrapperにする
            s.runtimeTarget = wrapper;
        }
    }

    // 親か自分にRectMask2Dが付いてるならそれを返す
    private bool HasRectMaskSelfOrParent(GameObject go, out GameObject maskRoot)
    {
        maskRoot = null;
        if (!go) return false;

        var cur = go.transform;
        while (cur != null)
        {
            if (cur.GetComponent<RectMask2D>() != null)
            {
                maskRoot = cur.gameObject;
                return true;
            }
            cur = cur.parent;
        }
        return false;
    }

    // RectTransformのレイアウト情報をコピーする補助
    private void CopyRectTransform(RectTransform src, RectTransform dst)
    {
        dst.anchorMin = src.anchorMin;
        dst.anchorMax = src.anchorMax;
        dst.anchoredPosition = src.anchoredPosition;
        dst.sizeDelta = src.sizeDelta;
        dst.pivot = src.pivot;
        dst.localScale = src.localScale;
        dst.localRotation = src.localRotation;
    }

    // ============================================================
    // シーケンス
    // ============================================================
    private IEnumerator PlaySequence()
    {
        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];

            if (s.delayBeforeShow > 0f)
                yield return new WaitForSeconds(s.delayBeforeShow);

            yield return StartCoroutine(PlayStepReveal(s));
        }
    }

    private IEnumerator PlayStepReveal(ShowStep s)
    {
        var go = s.runtimeTarget ? s.runtimeTarget : s.target;
        if (!go) yield break;

        switch (s.mode)
        {
            case RevealMode.FadeIn:
                yield return StartCoroutine(FadeInObject(go, s.duration));
                break;

            case RevealMode.LeftToRight:
                yield return StartCoroutine(RevealMask_Grow(go, s.duration,
                    verticalMode: false,
                    growFromStartEdge: true,
                    diagonal: false));
                break;

            case RevealMode.RightToLeft:
                yield return StartCoroutine(RevealMask_Grow(go, s.duration,
                    verticalMode: false,
                    growFromStartEdge: false,   // 右から左
                    diagonal: false));
                break;

            case RevealMode.TopToBottom:
                yield return StartCoroutine(RevealMask_Grow(go, s.duration,
                    verticalMode: true,
                    growFromStartEdge: true,
                    diagonal: false));
                break;

            case RevealMode.BottomToTop:
                yield return StartCoroutine(RevealMask_Grow(go, s.duration,
                    verticalMode: true,
                    growFromStartEdge: false,
                    diagonal: false));
                break;

            case RevealMode.DiagonalTLBR:
                yield return StartCoroutine(RevealMask_Grow(go, s.duration,
                    verticalMode: false,
                    growFromStartEdge: true,
                    diagonal: true));
                break;
        }
    }

    // ============================================================
    // 初期で隠す
    // ============================================================
    private void HideInstant(ShowStep s)
    {
        var go = s.runtimeTarget ? s.runtimeTarget : s.target;
        if (!go) return;

        go.SetActive(true);

        // CanvasGroupを0に
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        // マスク広げ系のときは、開始サイズを0にしておく必要があるので
        // ここではまだ触らない。サイズ初期化は RevealMask_Grow() の頭でやる。
    }

    // ============================================================
    // フェードイン（αだけ上げる）
    // ============================================================
    private IEnumerator FadeInObject(GameObject go, float duration)
    {
        if (!go) yield break;

        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();

        go.SetActive(true);

        float t = 0f;
        float startA = 0f;
        float endA = 1f;
        cg.alpha = startA;

        if (duration <= 0f)
        {
            cg.alpha = endA;
            yield break;
        }

        while (t < duration)
        {
            t += Time.deltaTime;
            float lerp = Mathf.Clamp01(t / duration);
            cg.alpha = Mathf.Lerp(startA, endA, lerp);
            yield return null;
        }
        cg.alpha = endA;
    }

    // ============================================================
    // マスクで「横/縦/斜めに広がる」演出
    // ============================================================
    private IEnumerator RevealMask_Grow(
        GameObject wrapper,
        float duration,
        bool verticalMode,
        bool growFromStartEdge,
        bool diagonal
    )
    {
        if (!wrapper) yield break;

        var mask = wrapper.GetComponent<RectMask2D>();
        if (!mask)
        {
            // ここに来る時点でPrepareRuntimeTargets()がラップ済みのはずだけど、
            // 念のため保険
            mask = wrapper.AddComponent<RectMask2D>();
        }

        var wrapperRT = wrapper.GetComponent<RectTransform>();
        if (!wrapperRT) yield break;

        var cg = wrapper.GetComponent<CanvasGroup>();
        if (!cg) cg = wrapper.AddComponent<CanvasGroup>();

        wrapper.SetActive(true);

        // 最終サイズを覚える
        float fullW = wrapperRT.rect.size.x;
        float fullH = wrapperRT.rect.size.y;
        if (fullW < 0.0001f) fullW = 0.0001f;
        if (fullH < 0.0001f) fullH = 0.0001f;

        // 今のpivotを元に「どっちから伸びるか」決まる。
        // growFromStartEdge=true: pivotは"スタート側"
        // growFromStartEdge=false: pivotは"ゴール側"
        // 自動ラップしたときは pivot をそのままコピーしてるので、
        // 方向感を合わせたいなら、あらかじめ画像のpivotを左/右/上/下に寄せておくと自然になる。
        // （全部中央pivotでも動くけど、中央から広がる感じになる）

        Vector2 savedPivot = wrapperRT.pivot;

        // 開始状態: サイズ0
        // 横方向演出なら幅0、縦方向なら高さ0、
        // 斜めなら両方0から同時に伸ばす
        float startW = (diagonal || !verticalMode) ? 0f : fullW;
        float startH = (diagonal || verticalMode) ? 0f : fullH;

        float endW = fullW;
        float endH = fullH;

        // alphaも0からスタート
        cg.alpha = 0f;

        float t = 0f;
        if (duration <= 0f) duration = 0.0001f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float lerp = Mathf.Clamp01(t / duration);

            float w = Mathf.Lerp(startW, endW, lerp);
            float h = Mathf.Lerp(startH, endH, lerp);

            if (!diagonal)
            {
                if (verticalMode)
                {
                    // 上下系: 横は常にフル
                    w = endW;
                }
                else
                {
                    // 左右系: 縦は常にフル
                    h = endH;
                }
            }

            // サイズを更新
            wrapperRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            wrapperRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);

            // 透明度も同時に上げる
            cg.alpha = lerp;

            yield return null;
        }

        // 終了時はフルサイズ＆α1
        wrapperRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, endW);
        wrapperRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, endH);
        cg.alpha = 1f;

        // pivotは元の値のまま
        wrapperRT.pivot = savedPivot;
    }
}
