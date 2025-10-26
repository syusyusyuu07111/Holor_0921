using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class GameOverSequence : MonoBehaviour
{
    // どんな出し方をするか
    public enum RevealMode
    {
        FadeIn,             // ただのフェード(αだけ上げる)
        LeftToRight,        // 左→右に露出
        RightToLeft,        // 右→左に露出
        TopToBottom,        // 上→下に露出
        BottomToTop,        // 下→上に露出
        DiagonalTLBR,       // 左上→右下っぽく露出（縦横いっしょに広げる）
        ThrownIn            // 画面外から投げ込まれたみたいに飛んでくる
    }

    [System.Serializable]
    public class ShowStep
    {
        [Header("このタイミングで表示するUI(画像そのままでOK)")]
        public GameObject target;          // ここに表示したいImage等をそのまま入れる

        [Header("このステップの前に待つ秒数")]
        public float delayBeforeShow = 0.5f;

        [Header("演出タイプ")]
        public RevealMode mode = RevealMode.FadeIn;

        [Header("演出にかける時間(秒)")]
        public float duration = 0.4f;

        [Header("直前のステップと同時に再生する？")]
        public bool StartWithPreviousStep = false;

        // ---- ThrownIn 用パラメータ ----
        //   anchoredPosition(本来の場所) からこの分ズラした位置をスタート地点にする
        //   例: (-500, 200) なら左上の外から飛んでくる感じ
        [Header("【ThrownIn専用】開始オフセット(px)")]
        public Vector2 throwStartOffset = new Vector2(-500f, 200f);

        //   回転しながら飛んでくる。正なら反時計回りに回る
        [Header("【ThrownIn専用】最初の追加回転角(Z度)")]
        public float throwSpinDegrees = 360f;

        // ---- ランタイム用 ----
        [System.NonSerialized] public GameObject runtimeTarget; // Wrapperとか
    }

    [Header("順番に表示するリスト(上から順に再生)")]
    public ShowStep[] steps;

    [Header("最初に全部隠す")]
    public bool hideAllOnStart = true;

    [Header("Start時に自動再生")]
    public bool playOnStart = true;

    //==================================================
    // Start
    //==================================================
    void Start()
    {
        // 1) Wrapper の準備と並び順復元
        PrepareRuntimeTargets();

        // 2) 最初は全て非表示状態にする
        if (hideAllOnStart)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                HideInstant(steps[i]);
            }
        }

        // 3) 再生オプション
        if (playOnStart)
        {
            StartCoroutine(PlaySequence());
        }
    }

    public void PlayNow()
    {
        StartCoroutine(PlaySequence());
    }

    //==================================================
    // PrepareRuntimeTargets
    //
    // 各 target を、必要なら RectMask2D＋CanvasGroup を持つ
    // wrapper に包む。包んだあと siblingIndex(兄弟順) を
    // 元の場所に戻すことで描画順を崩さない。
    //==================================================
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

            // すでにどこか親に RectMask2D あるなら、それをそのまま runtimeTarget にする
            if (HasRectMaskSelfOrParent(s.target, out GameObject maskRoot))
            {
                s.runtimeTarget = maskRoot;
                continue;
            }

            // RectTransform がなければUIじゃないのでラップせずそのまま
            var rt = s.target.GetComponent<RectTransform>();
            if (!rt)
            {
                s.runtimeTarget = s.target;
                continue;
            }

            // ---- ラップ作成 ----
            // 元の親と並び順を覚えておく
            Transform oldParent = rt.parent;
            int oldSibling = rt.GetSiblingIndex();

            // wrapper生成
            GameObject wrapper = new GameObject(s.target.name + "_Wrapper");
            var wrapperRT = wrapper.AddComponent<RectTransform>();

            // wrapper を元の親の子にする
            wrapperRT.SetParent(oldParent, worldPositionStays: false);

            // レイアウトコピー
            CopyRectTransform(rt, wrapperRT);

            // wrapperにRectMask2DとCanvasGroupを追加
            wrapper.AddComponent<RectMask2D>();
            var cg = wrapper.AddComponent<CanvasGroup>();
            cg.alpha = 1f; // 一旦1。あとでHideInstantで0に落とす

            // 元の画像を wrapper の子へ移動
            rt.SetParent(wrapperRT, worldPositionStays: false);

            // ★描画順キープ
            wrapperRT.SetSiblingIndex(oldSibling);

            // これが以後アニメ対象
            s.runtimeTarget = wrapper;
        }
    }

    // 親か自分に RectMask2D が付いてればそれを返す
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

    // RectTransformのレイアウト情報をコピー
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

    //==================================================
    // シーケンス全体を再生
    //
    // StartWithPreviousStep=false:
    //   delay待って → アニメ再生 → 終わるまで待つ
    //
    // StartWithPreviousStep=true:
    //   直前ステップと完全に同時開始したい
    //   並列でコルーチン走らせて親は待たない
    //==================================================
    private IEnumerator PlaySequence()
    {
        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];

            if (s.StartWithPreviousStep && i > 0)
            {
                // 前のと同時に出したいので、並列で流すだけ
                StartCoroutine(PlayStepWithDelayThenReveal(s));
                // 親は待たない
                continue;
            }
            else
            {
                // 普通にこのステップが終わるまで待つ
                yield return PlayStepWithDelayThenReveal(s);
            }
        }
    }

    // 1ステップ分：delay → 演出
    private IEnumerator PlayStepWithDelayThenReveal(ShowStep s)
    {
        if (s == null) yield break;

        if (s.delayBeforeShow > 0f)
            yield return new WaitForSeconds(s.delayBeforeShow);

        yield return StartCoroutine(PlayStepReveal(s));
    }

    //==================================================
    // ステップ1個分の実行
    //==================================================
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
                    growFromStartEdge: false,   // 右→左
                    diagonal: false));
                break;

            case RevealMode.TopToBottom:
                yield return StartCoroutine(RevealMask_Grow(go, s.duration,
                    verticalMode: true,
                    growFromStartEdge: true,    // 上→下
                    diagonal: false));
                break;

            case RevealMode.BottomToTop:
                yield return StartCoroutine(RevealMask_Grow(go, s.duration,
                    verticalMode: true,
                    growFromStartEdge: false,   // 下→上
                    diagonal: false));
                break;

            case RevealMode.DiagonalTLBR:
                yield return StartCoroutine(RevealMask_Grow(go, s.duration,
                    verticalMode: false,
                    growFromStartEdge: true,    // 左上→右下イメージ
                    diagonal: true));
                break;

            case RevealMode.ThrownIn:
                yield return StartCoroutine(RevealThrownIn(go, s.duration, s.throwStartOffset, s.throwSpinDegrees));
                break;
        }
    }

    //==================================================
    // 最初に全部隠す
    //==================================================
    private void HideInstant(ShowStep s)
    {
        var go = s.runtimeTarget ? s.runtimeTarget : s.target;
        if (!go) return;

        go.SetActive(true);

        // αを0に落とす
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        // サイズの初期化（マスク方向のやつ）は
        // RevealMask_Grow 側で最初に組み立てるのでここではいじらない
    }

    //==================================================
    // フェードイン（αだけ上げる）
    //==================================================
    private IEnumerator FadeInObject(GameObject go, float duration)
    {
        if (!go) yield break;

        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();

        go.SetActive(true);

        float t = 0f;
        const float startA = 0f;
        const float endA = 1f;
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

    //==================================================
    // マスクで「横/縦/斜めに広がる」演出
    //
    // verticalMode=true  -> 上下方向に伸びる
    // verticalMode=false -> 左右方向に伸びる
    // diagonal=true      -> 縦横いっしょに0→フルで"斜め"っぽく
    //
    // ※pivotは触らないので、左から出したい/右から出したいは
    //   画像側のpivotでコントロールできる
    //==================================================
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
            // 念のため
            mask = wrapper.AddComponent<RectMask2D>();
        }

        var wrapperRT = wrapper.GetComponent<RectTransform>();
        if (!wrapperRT) yield break;

        var cg = wrapper.GetComponent<CanvasGroup>();
        if (!cg) cg = wrapper.AddComponent<CanvasGroup>();

        wrapper.SetActive(true);

        float fullW = wrapperRT.rect.size.x;
        float fullH = wrapperRT.rect.size.y;
        if (fullW < 0.0001f) fullW = 0.0001f;
        if (fullH < 0.0001f) fullH = 0.0001f;

        // pivot はそのまま保持
        Vector2 savedPivot = wrapperRT.pivot;

        // スタートは0サイズ
        float startW = (diagonal || !verticalMode) ? 0f : fullW;
        float startH = (diagonal || verticalMode) ? 0f : fullH;
        float endW = fullW;
        float endH = fullH;

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
                    // 縦方向演出なら 横幅は常にフル
                    w = endW;
                }
                else
                {
                    // 横方向演出なら 高さは常にフル
                    h = endH;
                }
            }

            wrapperRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            wrapperRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);

            cg.alpha = lerp;

            yield return null;
        }

        wrapperRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, endW);
        wrapperRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, endH);
        cg.alpha = 1f;

        wrapperRT.pivot = savedPivot;
    }

    //==================================================
    // ThrownIn
    //
    // ・最終位置(finalPos)と最終回転(finalRot)を覚える
    // ・そこから throwStartOffset 分ずらした場所 + 回転追加 からスタート
    // ・easeOutBackっぽい減速で final に吸い込まれる
    // ・αも0→1
    //
    // s.throwStartOffset で「どこから飛んでくるか」を決める
    // s.throwSpinDegrees で「どれくらい回転しながら飛ぶか」を決める
    //==================================================
    private IEnumerator RevealThrownIn(GameObject go, float duration, Vector2 startOffset, float spinDeg)
    {
        if (!go) yield break;

        var rt = go.GetComponent<RectTransform>();
        if (!rt)
        {
            // UIじゃない場合はフェードインだけやっとく
            yield return FadeInObject(go, duration);
            yield break;
        }

        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();

        go.SetActive(true);

        // ゴール（本来の）位置/回転
        Vector2 finalPos = rt.anchoredPosition;
        Quaternion finalRot = rt.localRotation;

        // スタート位置/回転
        Vector2 startPos = finalPos + startOffset;
        float finalZ = finalRot.eulerAngles.z;
        float startZ = finalZ + spinDeg;

        // 初期セット
        rt.anchoredPosition = startPos;
        rt.localRotation = Quaternion.Euler(0f, 0f, startZ);
        cg.alpha = 0f;

        if (duration <= 0f) duration = 0.0001f;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float raw = Mathf.Clamp01(t / duration);

            // ちょっと跳ねて止まる感じのイージング（easeOutBack系）
            float eased = EaseOutBack(raw);

            // 位置を補間
            Vector2 nowPos = Vector2.LerpUnclamped(startPos, finalPos, eased);
            rt.anchoredPosition = nowPos;

            // 回転も補間（スピンして止まる）
            float nowZ = Mathf.LerpUnclamped(startZ, finalZ, eased);
            rt.localRotation = Quaternion.Euler(0f, 0f, nowZ);

            // αはシンプルにリニアでOK
            cg.alpha = raw;

            yield return null;
        }

        // 最終状態を保証
        rt.anchoredPosition = finalPos;
        rt.localRotation = finalRot;
        cg.alpha = 1f;
    }

    // ちょっと跳ねてから止まる感じのイージング
    // いわゆる "easeOutBack"
    private float EaseOutBack(float x)
    {
        // 定番のパラメータ
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float t = x - 1f;
        return 1f + c3 * (t * t * t) + c1 * (t * t);
    }
}
