using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIOrbs : MonoBehaviour
{
    // ============================================================
    // UIOrbs.cs
    // 画像（オーブ）を「中心（RectTransform）」のまわりで周回させる
    // ・各オーブはランダム遅延で出現（enabled=true）して動き始める
    // ・角速度（deg/s）と半径はオーブごとにランダム
    // ・半径方向にPerlinNoiseで“ゆらぎ”（wobble）を足して生っぽくする
    // ・Time.unscaledTime / unscaledDeltaTime 使用（ポーズ中も動く）
    // ============================================================

    [Header("動かす Image のリスト")]
    public List<Image> targets = new List<Image>();   // オーブ画像

    [Header("各オーブの開始ディレイ（秒）")]
    public float firstMoveDelayMin = 0f;              // 最小
    public float firstMoveDelayMax = 0.8f;            // 最大（0にすれば即時）

    [Header("中心と動き")]
    public RectTransform center;                      // 周回の中心（幽霊など）
    public float radiusMin = 60f;
    public float radiusMax = 140f;
    public float angularDegMin = 10f;                 // [度/秒]
    public float angularDegMax = 30f;                 // [度/秒]
    public float wobble = 14f;                        // 半径方向のゆらぎ幅
    public float noiseSpeed = 0.6f;                   // ゆらぎスピード

    // オーブ1個ぶんのランタイム情報
    struct Item
    {
        public Image img;             // 対象Image（表示ON/OFF）
        public RectTransform rt;      // anchoredPositionをいじる
        public System.Random rng;     // ディレイなどの乱数（オーブごと固定）
        public float angle;           // 周回角 [rad]
        public float angVel;          // 角速度 [rad/s]
        public float baseR;           // 基本半径
        public float nseed;           // PerlinNoiseの種
        public bool active;           // 起動済み（動く/表示）
    }

    readonly List<Item> items = new List<Item>();

    // Inspector値の整合性を保つ（Editor上で値を触ったとき）
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
        // targets から items を生成して初期パラメータを決める
        items.Clear();

        foreach (var img in targets)
        {
            if (!img) continue;

            RectTransform rt = img.rectTransform;

            // オーブごとの乱数（同じ実行内で固定的に動くように）
            int seed = img.gameObject.GetInstanceID() ^ Environment.TickCount;
            var rng = new System.Random(seed);

            // 角度/角速度/半径/ノイズ種をランダム化
            float ang = Mathf.Deg2Rad * UnityEngine.Random.Range(0f, 360f);

            float aVel = Mathf.Deg2Rad *
                         UnityEngine.Random.Range(angularDegMin, angularDegMax) *
                         (UnityEngine.Random.value < 0.5f ? 1f : -1f); // 左回り/右回り

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
        // centerが無いと計算不能なので停止
        if (!center)
        {
            Debug.LogWarning("[UIOrbs] center を設定してください。");
            enabled = false;
            return;
        }

        // 起動前は見えない（出現後は消さない方針）
        foreach (var it in items)
            if (it.img) it.img.enabled = false;

        // 各オーブを「個別ディレイ」で起動
        for (int i = 0; i < items.Count; i++)
            StartCoroutine(ActivateAfterDelay(i));
    }

    // 指定indexのオーブをランダム遅延後に出現＆動作開始
    IEnumerator ActivateAfterDelay(int index)
    {
        var it = items[index];

        float d = RandomRange(it.rng, firstMoveDelayMin, firstMoveDelayMax);
        yield return new WaitForSeconds(d);

        if (it.img) it.img.enabled = true; // ここで出現
        it.active = true;

        items[index] = it;                 // structなので書き戻し必須
    }

    void Update()
    {
        if (!center) return;

        // 中心は anchoredPosition（同じ親座標系前提）
        Vector2 cpos = center.anchoredPosition;

        // unscaled：ポーズ中でも回る
        float t = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (!it.active) continue;

            // 周回角を進める
            it.angle += it.angVel * dt;

            // 半径のゆらぎ（PerlinNoise 0..1 → -1..1）
            float n = Mathf.PerlinNoise(it.nseed, t * noiseSpeed) * 2f - 1f;
            float r = it.baseR + n * wobble;

            // 位置計算（中心 + 円周座標）
            Vector2 pos = cpos + new Vector2(Mathf.Cos(it.angle), Mathf.Sin(it.angle)) * r;
            it.rt.anchoredPosition = pos;

            items[i] = it; // structなので更新を書き戻す
        }
    }

    // System.Random を使った Range（min==maxなら固定値）
    float RandomRange(System.Random rng, float min, float max)
    {
        if (Mathf.Approximately(min, max)) return min;
        double u = rng.NextDouble(); // 0..1
        return (float)(min + (max - min) * u);
    }
}