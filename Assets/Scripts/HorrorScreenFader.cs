using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/*
     画面フェード（ホラー演出付き）
     ・黒マスクImageでフェード
     ・URP Volumeがあれば Vignette / ChromaticAberration / FilmGrain と連動
     ・FadeIn / FadeOut / FadeAndLoad を static API で呼べる
*/

public class HorrorScreenFader : MonoBehaviour
{
    //================
    // Static API
    //================

    // フェード中か（外部からの二重呼び出し抑止などに使う）
    public static bool IsBusy => _inst && _inst._busy;

    /*
         画面を明るくする（黒→透明）
         ・duration: フェード時間
         ・vignettePulse: ビネットの脈動（任意）
         ・noiseFlicker: 透明度にノイズを混ぜる（任意）
    */
    public static void FadeIn(float duration = 1.2f, bool vignettePulse = true, bool noiseFlicker = true)
    {
        Ensure();
        _inst.StopAllCoroutines();
        _inst.StartCoroutine(_inst.CoFade(1f, 0f, duration, vignettePulse, noiseFlicker));
    }

    /*
         画面を暗くする（透明→黒）
         ・onDone: 終了後コールバック（任意）
    */
    public static void FadeOut(float duration = 1.0f, bool vignettePulse = true, bool noiseFlicker = true, System.Action onDone = null)
    {
        Ensure();
        _inst.StopAllCoroutines();
        _inst.StartCoroutine(_inst.CoFade(0f, 1f, duration, vignettePulse, noiseFlicker, onDone));
    }

    /*
         フェードアウト→シーンロード→フェードイン
         ・sceneName: LoadSceneAsyncに渡すシーン名
         ・fadeOut/fadeIn: 各フェード時間
    */
    public static void FadeAndLoad(string sceneName, float fadeOut = 1.1f, float fadeIn = 1.1f, bool vignettePulse = true, bool noiseFlicker = true)
    {
        Ensure();
        _inst.StopAllCoroutines();
        _inst.StartCoroutine(_inst.CoFadeAndLoad(sceneName, fadeOut, fadeIn, vignettePulse, noiseFlicker));
    }

    //================
    // Singleton Instance
    //================

    private static HorrorScreenFader _inst;

    private Canvas _canvas;
    private Image _mask;
    private bool _busy;

    //================
    // URP Volume（あれば演出をなじませる）
    //================

    private Vignette _vig; private bool _hasVig;
    private ChromaticAberration _ca; private bool _hasCA;
    private FilmGrain _grain; private bool _hasGrain;

    //================
    // Unity Lifecycle
    //================

    private void Awake()
    {
        // 同名が複数ある場合は先にいた方を残す
        if (_inst && _inst != this) { Destroy(gameObject); return; }

        _inst = this;
        DontDestroyOnLoad(gameObject);

        BuildCanvasIfNeeded();
        ResolveVolume();
    }

    //================
    // 初期化（static呼び出し用）
    //================

    /*
         static API から呼ばれた時の保険
         ・インスタンスが無ければ生成して常駐させる
         ・CanvasとVolume参照を確実に用意する
    */
    private static void Ensure()
    {
        if (_inst == null)
        {
            var go = new GameObject("[HorrorScreenFader]");
            _inst = go.AddComponent<HorrorScreenFader>();
            DontDestroyOnLoad(go);
        }

        _inst.BuildCanvasIfNeeded();
        _inst.ResolveVolume();
    }

    //================
    // Canvas生成（黒マスク）
    //================

    /*
         フェード用のCanvasと黒マスクImageを生成する
         ・ScreenSpaceOverlay / 最前面 / フルスクリーン
    */
    private void BuildCanvasIfNeeded()
    {
        if (_canvas != null) return;

        // Canvas
        GameObject cgo = new GameObject("Canvas");
        cgo.transform.SetParent(transform);
        _canvas = cgo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = short.MaxValue;                   // いちばん上
        cgo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cgo.AddComponent<GraphicRaycaster>();

        // Black full-screen Image
        GameObject imgGo = new GameObject("Mask");
        imgGo.transform.SetParent(cgo.transform, false);
        _mask = imgGo.AddComponent<Image>();
        _mask.color = new Color(0f, 0f, 0f, 1f);

        var rt = _mask.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    //================
    // URP Volume参照解決
    //================

    /*
         シーン内のGlobal Volumeを探して、ポストエフェクト参照を取る
         ・Vignette / ChromaticAberration / FilmGrain を使う（あれば）
         ・overrideStateが無い場合は0でOverrideしておく
    */
    private void ResolveVolume()
    {
        _hasVig = _hasCA = _hasGrain = false;

        Volume vol = FindFirstObjectByType<Volume>();             // どこかのGlobal Volume
        if (vol && vol.profile)
        {
            _hasVig = vol.profile.TryGet(out _vig);
            _hasCA = vol.profile.TryGet(out _ca);
            _hasGrain = vol.profile.TryGet(out _grain);

            if (_hasVig) { _vig.active = true; if (!_vig.intensity.overrideState) _vig.intensity.Override(0f); }
            if (_hasCA) { _ca.active = true; if (!_ca.intensity.overrideState) _ca.intensity.Override(0f); }
            if (_hasGrain) { _grain.active = true; if (!_grain.intensity.overrideState) _grain.intensity.Override(0f); }
        }
    }

    //================
    // Fade本体
    //================

    /*
         from→to の透明度でフェードする
         ・Time.unscaledDeltaTime を使う（停止中でも進む）
         ・vignettePulse: ビネットを呼吸させる
         ・noiseFlicker: 透明度に小さくノイズを混ぜる
         ・onDone: 終了後コールバック
    */
    private IEnumerator CoFade(float from, float to, float duration, bool vignettePulse, bool noiseFlicker, System.Action onDone = null)
    {
        _busy = true;
        float t = 0f;
        _mask.color = new Color(0f, 0f, 0f, from);

        // エフェクトの最大強度（フェード量aに合わせて掛ける）
        float vigBase = 0f;
        float vigMax = 0.5f;
        float caMax = 0.3f;
        float grainMax = 0.5f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);

            // 少しだけホラーな“呼吸”と、ちらつき
            float pulse = vignettePulse ? (0.5f + 0.5f * Mathf.Sin((Time.unscaledTime * 2.2f) + 1.3f)) : 0f;
            float noise = noiseFlicker ? (Mathf.PerlinNoise(Time.unscaledTime * 10f, 0.37f) * 0.2f) : 0f;

            float a = Mathf.Lerp(from, to, EaseInOutQuad(u));
            _mask.color = new Color(0f, 0f, 0f, a + noise * (to > from ? 0.25f : 0.12f));

            // URPエフェクトがあれば連動
            if (_hasVig) _vig.intensity.Override(Mathf.Clamp01(vigBase + pulse * 0.25f + a * vigMax));
            if (_hasCA) _ca.intensity.Override(a * caMax);
            if (_hasGrain) _grain.intensity.Override(a * grainMax);

            yield return null;
        }

        // 最終値を固定
        _mask.color = new Color(0f, 0f, 0f, to);
        if (_hasCA) _ca.intensity.Override(to * 0.3f);
        if (_hasGrain) _grain.intensity.Override(to * 0.5f);
        if (_hasVig) _vig.intensity.Override(Mathf.Clamp01(to * 0.5f));

        _busy = false;
        onDone?.Invoke();
    }

    //================
    // Fade + Scene Load
    //================

    /*
         フェードアウト → LoadSceneAsync → Volume取り直し → フェードイン
         ・シーンを跨ぐとVolume参照が変わる可能性があるので ResolveVolume() を呼ぶ
    */
    private IEnumerator CoFadeAndLoad(string scene, float fadeOut, float fadeIn, bool vignettePulse, bool noiseFlicker)
    {
        yield return CoFade(0f, 1f, fadeOut, vignettePulse, noiseFlicker);

        // ロード
        yield return SceneManager.LoadSceneAsync(scene);

        ResolveVolume();                                         // シーン跨ぎで取り直す
        yield return null;                                       // 1フレーム待ってから

        yield return CoFade(1f, 0f, fadeIn, vignettePulse, noiseFlicker);
    }

    //================
    // Easing
    //================

    // 0→1をなめらかにするイージング
    private static float EaseInOutQuad(float x)
        => (x < 0.5f) ? (2f * x * x) : (1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f);
}