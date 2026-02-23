using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using System.Collections;

public class GameOverSequence : MonoBehaviour
{
    // このスクリプトがやっていること
    // ゲームオーバー演出用のUIを、指定した順番で表示する
    // さらに「前のステップと同時に出す」指定ができる
    // 表示の仕方は Fade / マスク露出 / 投げ込み / 非表示 の複数モードを持つ
    //
    // 演出の流れ
    // 1 Startで表示対象を準備する（必要ならwrapperを作る）
    // 2 最初に全部を非表示にする（Hideモードだけは例外）
    // 3 stepsを先頭から処理し、同時再生グループ単位で再生する
    // 4 各ステップは delay → 指定モードの演出 を行う

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
        public GameObject target;                      // 実際に見せたいUI

        [Header("このステップを開始する前に待つ秒数")]
        public float delayBeforeShow = 0.5f;           // 演出開始前の待ち時間（Unscaled）

        [Header("同時再生フラグ (前のステップと同時に出したいならtrue)")]
        public bool playWithPrevious = false;          // trueなら直前ステップと同時開始

        [Header("演出タイプ")]
        public RevealMode mode = RevealMode.FadeIn;     // 表示の仕方

        [Header("演出にかける時間(秒)")]
        public float duration = 0.4f;                  // 演出の長さ（Unscaled）

        // ThrownIn 用パラメータ
        [Header("【ThrownIn専用】開始オフセット(画面外っぽい位置)")]
        public Vector2 thrownStartOffset = new Vector2(-800f, 300f); // 画面外スタート位置

        [Header("【ThrownIn専用】回転総量(度) 360以上で投げ感UP")]
        public float thrownSpinDegrees = 720f;         // 投げ込み中の回転量

        // runtimeTarget について
        // マスク露出系モードでは target を直接動かすのではなく
        // wrapper(RectMask2D付き) を作ってそれを動かす
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
    // wrapperを作ると階層が変わるので、順番崩れを防ぐために記録する
    // ------------------------------------------------
    private Dictionary<RectTransform, int> _originalSiblingIndex =
        new Dictionary<RectTransform, int>();

    //==================================================
    // Start
    // ここが「ゲームオーバー演出開始前の準備」担当
    //==================================================
    void Start()
    {
        // ステップごとに「実際に動かす対象(runtimeTarget)」を準備する
        // マスク演出の場合は wrapper を作ってそれをruntimeTargetにする
        PrepareRuntimeTargets();

        // 開始時に一旦全部隠す（Fadeのalpha=0にする）
        // Hideモードは「最初から表示されていて、このステップで消したい」ケースがあるので隠さない
        if (hideAllOnStart)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i].mode != RevealMode.Hide)
                {
                    HideInstant(steps[i]);
                }
            }
        }

        // 自動再生がONならここで演出を開始する
        if (playOnStart)
        {
            StartCoroutine(PlaySequence());
        }
    }

    //==================================================
    // 外から呼べる再生開始
    // ゲームオーバー発生タイミングで手動で呼ぶ用
    //==================================================
    public void PlayNow()
    {
        StartCoroutine(PlaySequence());
    }

    //==================================================
    // PrepareRuntimeTargets
    // 演出方式によって「動かす対象」を決める
    //
    // マスク露出系
    // targetを直接サイズ変更すると絵自体が伸びたり崩れるので
    // wrapper(RectMask2D付き) を作って wrapper のサイズを伸ばして見せる
    //
    // Fade / ThrownIn / Hide
    // これらはtarget自体を動かして問題ないので wrapper は作らない
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

            // マスク不要ならtargetそのものを動かす
            if (!needMask)
            {
                s.runtimeTarget = s.target;

                // FadeやThrownInでもalpha制御するのでCanvasGroupを必ず持たせる
                var cg = s.runtimeTarget.GetComponent<CanvasGroup>();
                if (!cg) cg = s.runtimeTarget.AddComponent<CanvasGroup>();

                CacheSiblingIndex(s.runtimeTarget);
                continue;
            }

            // ここから先は「マスク露出」演出

            // すでに親か自分がRectMask2Dを持っているなら新規生成せずそれを使う
            if (HasRectMaskSelfOrParent(s.target, out GameObject maskRoot))
            {
                s.runtimeTarget = maskRoot;
                CacheSiblingIndex(s.runtimeTarget);

                var cg = s.runtimeTarget.GetComponent<CanvasGroup>();
                if (!cg) cg = s.runtimeTarget.AddComponent<CanvasGroup>();

                continue;
            }

            // UI(RectTransform)じゃない場合はマスク演出できないのでtargetをそのまま使う
            var rt = s.target.GetComponent<RectTransform>();
            if (!rt)
            {
                s.runtimeTarget = s.target;
                CacheSiblingIndex(s.runtimeTarget);

                var cg = s.runtimeTarget.GetComponent<CanvasGroup>();
                if (!cg) cg = s.runtimeTarget.AddComponent<CanvasGroup>();

                continue;
            }

            // マスク用wrapperを新規作成する
            // wrapperを元の親に差し込み、targetをwrapperの子にすることで
            // wrapperサイズ変更 = 表示領域変更 になる
            Transform origParent = rt.parent;
            int origSibling = rt.GetSiblingIndex();

            GameObject wrapper = new GameObject(s.target.name + "_Wrapper");
            var wrapperRT = wrapper.AddComponent<RectTransform>();

            // 親に同じ兄弟順で差し込む（描画順を変えない）
            wrapperRT.SetParent(origParent, worldPositionStays: false);
            wrapperRT.SetSiblingIndex(origSibling);

            // wrapperをtargetと同じ見た目設定にする（位置やアンカーを一致させる）
            CopyRectTransform(rt, wrapperRT);

            // wrapperは「見せる窓」なのでRectMask2Dを付ける
            // alphaも扱うのでCanvasGroupも付ける
            wrapper.AddComponent<RectMask2D>();
            var cgWrapper = wrapper.AddComponent<CanvasGroup>();
            cgWrapper.alpha = 1f;

            // targetをwrapperの中に入れる
            rt.SetParent(wrapperRT, worldPositionStays: false);

            s.runtimeTarget = wrapper;

            CacheSiblingIndex(wrapper);
        }
    }

    //==================================================
    // 兄弟インデックスを覚えておく
    // wrapper生成などで順番がズレても戻せるようにする
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
    // 記録されている兄弟順に戻す
    // 演出開始前に呼んで「急に前後関係が変わる」を防ぐ
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
    // RectTransformをコピーする
    // wrapperを作った時に見た目がズレないようにする
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
    // 親か自分にRectMask2Dがあるか
    // すでにマスク構造があるUIの場合にwrapperを二重で作らないため
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
    // PlaySequence
    // ゲームオーバー演出の本体
    //
    // stepsを順番に処理しつつ
    // playWithPrevious が続く範囲を「同時再生グループ」としてまとめて再生する
    //==================================================
    private IEnumerator PlaySequence()
    {
        int i = 0;
        while (i < steps.Length)
        {
            // iから始まる「同時再生グループ」を作る
            List<ShowStep> batch = new List<ShowStep>();
            batch.Add(steps[i]);

            int j = i + 1;
            while (j < steps.Length && steps[j].playWithPrevious)
            {
                batch.Add(steps[j]);
                j++;
            }

            // グループ内は同時に開始し、全員が終わるまで待つ
            yield return StartCoroutine(PlayBatch(batch));

            // 次の未処理ステップへ
            i = j;
        }
    }

    //==================================================
    // PlayBatch
    // 同時再生グループをまとめて再生する
    //==================================================
    private IEnumerator PlayBatch(List<ShowStep> batch)
    {
        if (batch == null || batch.Count == 0) yield break;

        List<Coroutine> running = new List<Coroutine>();

        // 全員を開始する
        for (int k = 0; k < batch.Count; k++)
        {
            var s = batch[k];
            Coroutine co = StartCoroutine(PlaySingleStep(s));
            running.Add(co);
        }

        // 全員の終了を待つ
        foreach (var c in running)
        {
            if (c != null)
                yield return c;
        }
    }

    //==================================================
    // PlaySingleStep
    // 1ステップ分の実行
    // delay → 指定演出 の順で行う
    //==================================================
    private IEnumerator PlaySingleStep(ShowStep s)
    {
        if (!s.target && !s.runtimeTarget) yield break;

        // 演出前の待ち時間（timeScaleに影響されない）
        if (s.delayBeforeShow > 0f)
            yield return new WaitForSecondsRealtime(s.delayBeforeShow);

        // 指定モードの演出を実行
        yield return StartCoroutine(PlayStepReveal(s));
    }

    //==================================================
    // PlayStepReveal
    // modeに応じて「どう見せるか」を分岐する入口
    //==================================================
    private IEnumerator PlayStepReveal(ShowStep s)
    {
        var go = s.runtimeTarget ? s.runtimeTarget : s.target;
        if (!go) yield break;

        // 途中で順序がズレないように復元
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
    // HideInstant
    // 開始時に一瞬で非表示にする
    // ここではGameObjectはactiveのまま、alphaだけ0にする
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
    // FadeInObject
    // alphaを0→1にして表示する（timeScale無視）
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
    // FadeOutObject
    // alphaを現在→0にして消す（Hideモード用）
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
    // RevealMask_Grow
    // wrapper(RectMask2D付き) のサイズを0→フルにして
    // 中身が「露出していく」ように見せる
    //
    // verticalMode
    // true なら上下方向の露出
    // false なら左右方向の露出
    //
    // diagonal
    // trueなら横と縦を同時に伸ばして斜めっぽい露出にする
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
    // 画面外スタート → 指定位置へ移動しながら回転 → 着弾後に小さく揺らす
    //==================================================
    private IEnumerator ThrownInObject(GameObject go, float duration, Vector2 startOffset, float spinDegrees)
    {
        if (!go) yield break;

        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();

        var rt = go.GetComponent<RectTransform>();
        if (!rt)
        {
            // UI以外なら投げ込み演出できないのでフェードで代用
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

        // 着弾後の「ドンッ」を追加して投げ感を出す
        yield return StartCoroutine(HitImpactJiggle(rt));
    }

    //==================================================
    // HitImpactJiggle
    // 着弾の勢い表現
    // 1 潰れる（スケール変形）
    // 2 減衰しながら位置と回転が揺れる
    //==================================================
    private IEnumerator HitImpactJiggle(RectTransform rt)
    {
        if (!rt) yield break;

        Vector2 basePos = rt.anchoredPosition;
        Quaternion baseRot = rt.localRotation;
        Vector3 baseScale = rt.localScale;

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

        float shakeDuration = 0.08f;
        float shakePos = 8f;
        float shakeRot = 8f;
        t = 0f;
        while (t < shakeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - (t / shakeDuration);

            float offX = Random.Range(-1f, 1f) * shakePos * k;
            float offY = Random.Range(-1f, 1f) * shakePos * k;
            float offR = Random.Range(-1f, 1f) * shakeRot * k;

            rt.anchoredPosition = basePos + new Vector2(offX, offY);
            rt.localRotation = Quaternion.Euler(
                0f, 0f,
                baseRot.eulerAngles.z + offR
            );

            rt.localScale = Vector3.Lerp(new Vector3(1.1f, 0.85f, 1f), baseScale, 1f - k);

            yield return null;
        }

        rt.anchoredPosition = basePos;
        rt.localRotation = baseRot;
        rt.localScale = baseScale;
    }
}