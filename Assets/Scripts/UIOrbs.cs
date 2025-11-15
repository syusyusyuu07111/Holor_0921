using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;

public class UIOrbs : MonoBehaviour
{
    [Header("動かす Image のリスト")]
    public List<Image> targets = new List<Image>();   // オーブ画像

    [Header("各オーブの開始ディレイ（秒）")]
    public float firstMoveDelayMin = 0f;              // 最小
    public float firstMoveDelayMax = 0.8f;            // 最大（0にすれば即時）

    [Header("中心と動き")]
    public RectTransform center;                      // 幽霊などの中心
    public float radiusMin = 60f;
    public float radiusMax = 140f;
    public float angularDegMin = 10f;                 // [度/秒]
    public float angularDegMax = 30f;                 // [度/秒]
    public float wobble = 14f;                        // 半径方向のゆらぎ幅
    public float noiseSpeed = 0.6f;                   // ゆらぎスピード

    struct Item
    {
        public Image img;
        public RectTransform rt;
        public System.Random rng;
        public float angle;     // [rad]
        public float angVel;    // [rad/s]
        public float baseR;
        public float nseed;     // ノイズ用
        public bool active;     // 動き開始済み
    }

    readonly List<Item> items = new List<Item>();

    void OnValidate()
    {
        if (firstMoveDelayMin > firstMoveDelayMax)
            (firstMoveDelayMin, firstMoveDelayMax) = (firstMoveDelayMax, firstMoveDelayMin);
        if (radiusMin > radiusMax)
            (radiusMin, radiusMax) = (radiusMax, radiusMin);
        firstMoveDelayMin = Mathf.Max(0f, firstMoveDelayMin);
        firstMoveDelayMax = Mathf.Max(0f, firstMoveDelayMax);
    }

    void Awake()
    {
        items.Clear();
        foreach (var img in targets)
        {
            if (!img) continue;

            var rt = img.rectTransform;
            int seed = img.gameObject.GetInstanceID() ^ Environment.TickCount;
            var rng = new System.Random(seed);

            float ang = Mathf.Deg2Rad * UnityEngine.Random.Range(0f, 360f);
            float aVel = Mathf.Deg2Rad * UnityEngine.Random.Range(angularDegMin, angularDegMax)
                          * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
            float r = UnityEngine.Random.Range(radiusMin, radiusMax);
            float nseed = UnityEngine.Random.Range(0f, 1000f);

            items.Add(new Item
            {
                img = img,
                rt = rt,
                rng = rng,
                angle = ang,
                angVel = aVel,
                baseR = r,
                nseed = nseed,
                active = false
            });
        }
    }

    void Start()
    {
        if (!center) { Debug.LogWarning("[UIOrbs] center を設定してください。"); enabled = false; return; }

        // 最初は見えない状態にしておく（出現後は消さない）
        foreach (var it in items) if (it.img) it.img.enabled = false;

        // 各オーブをランダムな遅延で起動
        for (int i = 0; i < items.Count; i++)
            StartCoroutine(ActivateAfterDelay(i));
    }

    IEnumerator ActivateAfterDelay(int index)
    {
        var it = items[index];
        float d = RandomRange(it.rng, firstMoveDelayMin, firstMoveDelayMax);
        yield return new WaitForSeconds(d);

        if (it.img) it.img.enabled = true; // ここで出現し、その後は消さない
        it.active = true;
        items[index] = it;
    }

    void Update()
    {
        if (!center) return;

        Vector2 cpos = center.anchoredPosition;
        float t = Time.unscaledTime;

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (!it.active) continue;

            it.angle += it.angVel * Time.unscaledDeltaTime;

            // 半径のゆらぎ
            float n = Mathf.PerlinNoise(it.nseed, t * noiseSpeed) * 2f - 1f; // [-1,1]
            float r = it.baseR + n * wobble;

            Vector2 pos = cpos + new Vector2(Mathf.Cos(it.angle), Mathf.Sin(it.angle)) * r;
            it.rt.anchoredPosition = pos;

            items[i] = it;
        }
    }

    float RandomRange(System.Random rng, float min, float max)
    {
        if (Mathf.Approximately(min, max)) return min;
        double u = rng.NextDouble();
        return (float)(min + (max - min) * u);
    }
}
