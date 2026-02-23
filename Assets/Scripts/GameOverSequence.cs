using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using System.Collections;

public class GameOverSequence : MonoBehaviour
{
    // ------------------------------------------------
    // どう出すかのモード
    // ------------------------------------------------
    public enum RevealMode
    {
        FadeIn,             // フェード(αだけ上げる)
        LeftToRight,        // 左→右にニョキッと出る
        RightToLeft,        // 右→左にニョキッと出る
        TopToBottom,        // 上→下にニョキッと出る
        BottomToTop,        // 下→上にニョキッと出る
        DiagonalTLBR,       // 左上→右下っぽく同時に広がる
        ThrownIn,           // 画面外からぶん投げられてドンッと当たる
        Hide                // このステップの番で消す（フェードアウト）
    }

    // ------------------------------------------------
    // 1個のステップに対しての設定
    // ------------------------------------------------
    [System.Serializable]
    public class ShowStep
    {
        [Header("このステップで出すUIオブジェクト(画像そのものでOK)")]
        public GameObject target;                      // 画像オブジェクト本体をそのまま入れる

        [Header("このステップを開始する前に待つ秒数")]
        public float delayBeforeShow = 0.5f;

        [Header("同時再生フラグ (前のステップと同時に出したいならtrue)")]
        public bool playWithPrevious = false;
        // false: このステップは単独の開始タイミングを持つ
        // true : このステップは「直前のステップ」と同じタイミングで同時に開始する

        [Header("演出タイプ")]
        public RevealMode mode = RevealMode.FadeIn;

        [Header("演出にかける時間(秒)")]
        public float duration = 0.4f;

        // ---- ThrownIn 用パラメータ ----
        [Header("【ThrownIn専用】開始オフセット(画面外っぽい位置)")]
        public Vector2 thrownStartOffset = new Vector2(-800f, 300f);

        [Header("【ThrownIn専用】回転総量(度) 360以上で投げ感UP")]
        public float thrownSpinDegrees = 720f;

        // ランタイム用：実際にアニメさせる先(= RectMask2D 付きの wrapper 等)
        [System.NonSerialized] public GameObject runtimeTarget;
    }

    // ------------------------------------------------
    // 再生順リスト
    // ------------------------------------------------
    [Header("順番に(あるいは同時に)出すリスト")]
    public ShowStep[] steps;

    [Header("最初に全部隠す")]
    public bool hideAllOnStart = true;

    [Header("Start()で自動再生する")]
    public bool playOnStart = true;

    // ------------------------------------------------
    // 内部: もとの描画順(兄弟インデックス)保持用
    // ------------------------------------------------
    private Dictionary<RectTransform, int> _originalSiblingIndex =
        new Dictionary<RectTransform, int>();

    //==================================================
    // Start
    //==================================================
    void Start()
    {
        PrepareRuntimeTargets();

        if (hideAllOnStart)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                // Hide のステップは「最初から表示されていてほしい」ので隠さない
                if (steps[i].mode != RevealMode.Hide)
                {
                    HideInstant(steps[i]);
                }
            }
        }

        if (playOnStart)
        {
            StartCoroutine(PlaySequence());
        }
    }

    //==================================================
    // 外から呼べる再生開始
    //==================================================
    public void PlayNow()
    {
        StartCoroutine(PlaySequence());
    }

    //==================================================
    // PrepareRuntimeTargets
    //
    // ・マスク演出が必要なモードだけ wrapper + RectMask2D を作る
    // ・Fade / ThrownIn / Hide は target そのものを使う（wrapper 生成しない）
    //==================================================
    private void PrepareRuntimeTargets()
    {
        _originalSiblingIndex.Clear();

        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            if (!s.target)
            {
                s.runtimeTarget = null;
                continue;
            }

            bool needMask =
                (s.mode == RevealMode.LeftToRight) ||
                (s.mode == RevealMode.RightToLeft) ||
                (s.mode == RevealMode.TopToBottom) ||
                (s.mode == RevealMode.BottomToTop) ||
                (s.mode == RevealMode.DiagonalTLBR);

            // マスク不要ならそのまま使う
            if (!needMask)
            {
                s.runtimeTarget = s.target;

                // フェード用に CanvasGroup だけ用意
                var cg = s.runtimeTarget.GetComponent<CanvasGroup>();
                if (!cg) cg = s.runtimeTarget.AddComponent<CanvasGroup>();

                CacheSiblingIndex(s.runtimeTarget);
                continue;
            }

            // ここから先はマスク演出ありのケース

            // すでに RectMask2D を持つ親/自分があるならそれを使う
            if (HasRectMaskSelfOrParent(s.target, out GameObject maskRoot))
            {
                s.runtimeTarget = maskRoot;
                CacheSiblingIndex(s.runtimeTarget);

                var cg = s.runtimeTarget.GetComponent<CanvasGroup>();
                if (!cg) cg = s.runtimeTarget.AddComponent<CanvasGroup>();

                continue;
            }

            // target が UI (RectTransform) じゃない場合はそのまま使う
            var rt = s.target.GetComponent<RectTransform>();
            if (!rt)
            {
                s.runtimeTarget = s.target;
                CacheSiblingIndex(s.runtimeTarget);

                var cg = s.runtimeTarget.GetComponent<CanvasGroup>();
                if (!cg) cg = s.runtimeTarget.AddComponent<CanvasGroup>();

                continue;
            }

            // wrapper を作る
            Transform origParent = rt.parent;
            int origSibling = rt.GetSiblingIndex();

            GameObject wrapper = new GameObject(s.target.name + "_Wrapper");
            var wrapperRT = wrapper.AddComponent<RectTransform>();

            // wrapper を元の親に同じ位置で差し込む
            wrapperRT.SetParent(origParent, worldPositionStays: false);
            wrapperRT.SetSiblingIndex(origSibling); // 兄弟順を合わせる

            // 見た目(Anchor/Pivot/Pos/Size/Scale/Rot)をコピー
            CopyRectTransform(rt, wrapperRT);

            // wrapper 側にRectMask2DとCanvasGroup
            var mask = wrapper.AddComponent<RectMask2D>();
            var cgWrapper = wrapper.AddComponent<CanvasGroup>();
            cgWrapper.alpha = 1f; // 初期は1。HideInstant側で0にする

            // target を wrapper の子に
            rt.SetParent(wrapperRT, worldPositionStays: false);

            // ランタイムターゲットとしてこれを使う
            s.runtimeTarget = wrapper;

            CacheSiblingIndex(wrapper);
        }
    }

    //==================================================
    // 兄弟インデックスを覚えておく (順番崩れ防止)
    //==================================================
    private void CacheSiblingIndex(GameObject go)
    {
        if (!go) return;
        var rt = go.GetComponent<RectTransform>();
        if (!rt) return;
        if (!_originalSiblingIndex.ContainsKey(rt))
        {
            _originalSiblingIndex.Add(rt, rt.GetSiblingIndex());
        }
    }

    //==================================================
    // 後からでも元の順序を復元できるようにする（必要なら呼ぶ）
    //==================================================
    private void RestoreSiblingOrderIfRecorded(GameObject go)
    {
        if (!go) return;
        var rt = go.GetComponent<RectTransform>();
        if (!rt) return;
        if (_originalSiblingIndex.TryGetValue(rt, out int idx))
        {
            rt.SetSiblingIndex(idx);
        }
    }

    //==================================================
    // RectTransform情報をコピーする補助
    //==================================================
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
    // 親か自分に RectMask2D があるかチェック
    //==================================================
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

    //==================================================
    // シーケンス再生
    //==================================================
    private IEnumerator PlaySequence()
    {
        int i = 0;
        while (i < steps.Length)
        {
            // このステップから"同時"で出すバッチを作る
            List<ShowStep> batch = new List<ShowStep>();
            batch.Add(steps[i]);

            int j = i + 1;
            while (j < steps.Length && steps[j].playWithPrevious)
            {
                batch.Add(steps[j]);
                j++;
            }

            // バッチ全員を同時に開始して、全員の演出完了を待つ
            yield return StartCoroutine(PlayBatch(batch));

            // 次の未処理インデックスへ
            i = j;
        }
    }

    //==================================================
    // バッチ同時再生
    //==================================================
    private IEnumerator PlayBatch(List<ShowStep> batch)
    {
        if (batch == null || batch.Count == 0) yield break;

        List<Coroutine> running = new List<Coroutine>();

        for (int k = 0; k < batch.Count; k++)
        {
            var s = batch[k];
            Coroutine co = StartCoroutine(PlaySingleStep(s));
            running.Add(co);
        }

        foreach (var c in running)
        {
            if (c != null)
                yield return c;
        }
    }

    //==================================================
    // 単一ステップの実行
    //==================================================
    private IEnumerator PlaySingleStep(ShowStep s)
    {
        if (!s.target && !s.runtimeTarget) yield break;

        // 開始前の待ち（Unscaled）
        if (s.delayBeforeShow > 0f)
            yield return new WaitForSecondsRealtime(s.delayBeforeShow);

        // 実際の表示演出
        yield return StartCoroutine(PlayStepReveal(s));
    }

    //==================================================
    // あるステップに対して、指定されたRevealModeで表示演出
    //==================================================
    private IEnumerator PlayStepReveal(ShowStep s)
    {
        var go = s.runtimeTarget ? s.runtimeTarget : s.target;
        if (!go) yield break;

        RestoreSiblingOrderIfRecorded(go);

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
                    growFromStartEdge: false,
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

            case RevealMode.ThrownIn:
                yield return StartCoroutine(ThrownInObject(
                    go,
                    s.duration,
                    s.thrownStartOffset,
                    s.thrownSpinDegrees
                ));
                break;

            case RevealMode.Hide:
                yield return StartCoroutine(FadeOutObject(go, s.duration));
                break;
        }
    }

    //==================================================
    // 最初に一瞬で隠す
    //==================================================
    private void HideInstant(ShowStep s)
    {
        var go = s.runtimeTarget ? s.runtimeTarget : s.target;
        if (!go) return;

        go.SetActive(true);

        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
    }

    //==================================================
    // 単純フェードイン（UnscaledTime）
    //==================================================
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
            t += Time.unscaledDeltaTime;
            float lerp = Mathf.Clamp01(t / duration);
            cg.alpha = Mathf.Lerp(startA, endA, lerp);
            yield return null;
        }
        cg.alpha = endA;
    }

    //==================================================
    // 単純フェードアウト（Hide 用）
    //==================================================
    private IEnumerator FadeOutObject(GameObject go, float duration)
    {
        if (!go) yield break;

        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();

        go.SetActive(true);

        float t = 0f;
        float startA = cg.alpha;
        float endA = 0f;

        if (duration <= 0f)
        {
            cg.alpha = endA;
            yield break;
        }

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float lerp = Mathf.Clamp01(t / duration);
            cg.alpha = Mathf.Lerp(startA, endA, lerp);
            yield return null;
        }
        cg.alpha = endA;
    }

    //==================================================
    // マスクを使って横/縦/斜めに露出させる
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

        float startW = (diagonal || !verticalMode) ? 0f : fullW;
        float startH = (diagonal || verticalMode) ? 0f : fullH;

        float endW = fullW;
        float endH = fullH;

        cg.alpha = 0f;

        if (duration <= 0f) duration = 0.0001f;
        float t = 0f;

        Vector2 savedPivot = wrapperRT.pivot;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float lerp = Mathf.Clamp01(t / duration);

            float w = Mathf.Lerp(startW, endW, lerp);
            float h = Mathf.Lerp(startH, endH, lerp);

            if (!diagonal)
            {
                if (verticalMode)
                {
                    w = endW;
                }
                else
                {
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
    // ThrownInObject
    //==================================================
    private IEnumerator ThrownInObject(GameObject go, float duration, Vector2 startOffset, float spinDegrees)
    {
        if (!go) yield break;

        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();

        var rt = go.GetComponent<RectTransform>();
        if (!rt)
        {
            // UIじゃない場合はフェードインだけ fallback
            yield return StartCoroutine(FadeInObject(go, duration));
            yield break;
        }

        go.SetActive(true);

        Vector2 basePos = rt.anchoredPosition;
        Quaternion baseRot = rt.localRotation;
        Vector3 baseScale = rt.localScale;

        Vector2 startPos = basePos + startOffset;
        Quaternion startRot = Quaternion.Euler(0f, 0f, baseRot.eulerAngles.z + spinDegrees);

        rt.anchoredPosition = startPos;
        rt.localRotation = startRot;
        rt.localScale = baseScale;

        cg.alpha = 0f;

        if (duration <= 0f) duration = 0.0001f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float lerp = Mathf.Clamp01(t / duration);

            rt.anchoredPosition = Vector2.Lerp(startPos, basePos, lerp);
            rt.localRotation = Quaternion.Slerp(startRot, baseRot, lerp);
            cg.alpha = lerp;

            yield return null;
        }

        rt.anchoredPosition = basePos;
        rt.localRotation = baseRot;
        rt.localScale = baseScale;
        cg.alpha = 1f;

        yield return StartCoroutine(HitImpactJiggle(rt));
    }

    //==================================================
    // HitImpactJiggle
    //==================================================
    private IEnumerator HitImpactJiggle(RectTransform rt)
    {
        if (!rt) yield break;

        Vector2 basePos = rt.anchoredPosition;
        Quaternion baseRot = rt.localRotation;
        Vector3 baseScale = rt.localScale;

        // --- 1. 着弾直後のつぶれ ---
        float squashTime = 0.06f;
        float t = 0f;
        while (t < squashTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / squashTime);

            float sx = Mathf.Lerp(1f, 1.1f, k);
            float sy = Mathf.Lerp(1f, 0.85f, k);
            rt.localScale = new Vector3(sx, sy, 1f);

            yield return null;
        }

        // --- 2. 減衰しながら揺れ戻り ---
        float shakeDuration = 0.08f;
        float shakePos = 8f;
        float shakeRot = 8f;
        t = 0f;
        while (t < shakeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - (t / shakeDuration); // だんだん0に近づく

            float offX = Random.Range(-1f, 1f) * shakePos * k;
            float offY = Random.Range(-1f, 1f) * shakePos * k;
            float offR = Random.Range(-1f, 1f) * shakeRot * k;

            rt.anchoredPosition = basePos + new Vector2(offX, offY);
            rt.localRotation = Quaternion.Euler(
                0f, 0f,
                baseRot.eulerAngles.z + offR
            );

            // スケールは徐々に元に戻す
            rt.localScale = Vector3.Lerp(new Vector3(1.1f, 0.85f, 1f), baseScale, 1f - k);

            yield return null;
        }

        rt.anchoredPosition = basePos;
        rt.localRotation = baseRot;
        rt.localScale = baseScale;
    }
}
