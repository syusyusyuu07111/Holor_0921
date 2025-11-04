using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(10)]
public class GhostScreenOrbs : MonoBehaviour
{
    // ===== 参照 =====
    [Header("参照")]
    public EnemyAI Enemy;              // 出現判定＆最寄りゴースト
    public Camera Cam;                 // 未設定なら自動でCamera.main
    public GameObject OrbPrefab;       // 省略可（未設定なら自動生成）

    // 接近度を読むため（任意）
    [Tooltip("接近度(GetDangerBlend01)を読む参照。未設定でもOK")]
    public GameOver DangerRef;         // ★追加

    // ===== スポーン設定 =====
    [Header("スポーン")]
    public float SpawnPerSecond = 6f;  // 出現中の毎秒スポーン数
    public float SpawnJitter = 0.35f;  // 時間ばらつき（0..1）
    public Vector2 ViewportMargin = new Vector2(0.12f, 0.18f); // 画面端から内側に

    // ===== 位置/サイズ/速度 =====
    [Header("距離/サイズ/速度")]
    public float PlaneDistance = 0.45f;           // カメラ前の距離（m）
    public Vector2 StartSize = new Vector2(0.06f, 0.12f);
    public Vector2 Speed = new Vector2(0.35f, 0.65f);          // m/s
    public float DriftNoise = 0.25f;                           // 横ズレ

    // ===== 進行方向 =====
    [Header("進行方向")]
    [Tooltip("trueなら画面のY(上下)にだけ流れる。falseなら従来挙動")]
    public bool MoveAlongScreenY = true;   // 上下だけ
    [Tooltip("trueなら上下どちらにも飛ぶ。falseなら上方向のみ")]
    public bool RandomizeUpDown = false;   // 上下ランダム
    [Tooltip("trueならワールドのY(上)にだけ流す（ScreenYより優先）")]
    public bool MoveAlongWorldY = false;   // 世界Y優先
    [Tooltip("ゴースト方向への引力（従来挙動時のみ有効）")]
    public float GravityTowardGhost = 0.6f;

    // ===== 寿命/見た目 =====
    [Header("寿命/透明度")]
    public Vector2 Lifetime = new Vector2(0.9f, 1.6f);
    public AnimationCurve AlphaOverLife = AnimationCurve.EaseInOut(0, 0, 1, 0); // 端から端までフェード
    public AnimationCurve ScaleOverLife = AnimationCurve.EaseInOut(0, 1, 1, 0.6f);

    // ===== 強度（距離で増減） =====
    [Header("出現中の強度")]
    [Range(0, 1)] public float MinPresence = 0.25f; // 出現中は最低これだけ出す
    public float DistanceBoostRadius = 10f;        // 近いほど増える半径

    //接近時の見え方ブースト
    [Header("接近ブースト")]
    [Tooltip("接近時にスポーンレートへ足す倍率（例 0.75 → +75%）")]
    public float DangerSpawnBoost = 0.75f;         // ★追加
    [Tooltip("接近時のスケール乗算（例 1.15 → +15%）")]
    public float ScaleByDanger = 1.15f;            // ★追加

    // ===== デプス固定 =====
    [Header("深度ロック")]
    [Tooltip("trueならカメラが動いても奥行きを常に PlaneDistance に固定する")]
    public bool KeepDepthLocked = true;            // ★追加：デフォルトON

    // ===== プール =====
    [Header("プール")]
    public int PoolSize = 32;

    //れイヤ制御（プールは常に非表示レイヤ）
    [Header("レイヤ")]
    [Tooltip("プール中に使う隠しレイヤ名（このレイヤはカメラのCulling Maskから除外しておく）")]
    public string HiddenLayerName = "PooledHidden";    // ★追加
    [Tooltip("表示時に戻すレイヤ名")]
    public string VisibleLayerName = "Default";        // ★追加

    //見た目
    [Header("見た目")]
    [Tooltip("trueなら“本体メッシュ（球体）”は常に非表示。トレイルのみ表示")]
    public bool HideCoreMesh = true;                   // ★追加：本体を消す

    // ---- 内部 ----
    private float _accum;
    private readonly List<Orb> _pool = new List<Orb>();
    private Transform _ghostT;         // 直近のゴースト
    private Transform _poolRoot;       // プール親（ヒエラ非表示）
    private bool _initialized = false; // 幽霊が出るまで生成しない → ★Startで生成に変更
    private int _hiddenLayer = -1;
    private int _visibleLayer = -1;

    // 内部: 接近度キャッシュ（0..1）
    private float _danger01 = 0f;

    void Awake()
    {
        if (!Cam) Cam = Camera.main;
        // ※Awakeでは何も生成しない（遅延初期化）
    }

    // 起動時に必ずプールだけは作る（描画はOFF&隠しレイヤ）
    void Start()
    {
        _hiddenLayer = LayerMask.NameToLayer(HiddenLayerName);
        _visibleLayer = LayerMask.NameToLayer(VisibleLayerName);

        if (!_initialized)
        {
            EnsurePrefab();
            BuildPool();
            _initialized = true;
        }
    }

    //再有効化時も保険でプールを見えなくする
    void OnEnable()
    {
        if (_pool != null)
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i] != null && _pool[i].host)
                    SetRenderable(_pool[i].host, false);
        }
    }

    void Update()
    {
        if (!Enemy || !Cam) return;

        // いま出現中？
        bool present = (Enemy.CurrentGhost != null);
        _ghostT = present ? Enemy.CurrentGhost.transform : null;

        // Startで初期化するのでここでは不要だが、保険
        if (!_initialized) return; // まだ一度も出てない

        // 出現中の目標発生レート（距離で強弱）
        float presence = present ? 1f : 0f;
        if (presence > 0f && _ghostT)
        {
            float d = Vector3.Distance(Cam.transform.position, _ghostT.position);
            float w = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, DistanceBoostRadius));
            presence = Mathf.Max(MinPresence, w);
        }

        // 接近度（0..1）
        _danger01 = (DangerRef ? Mathf.Clamp01(DangerRef.GetDangerBlend01()) : 0f); // ★追加

        // スポーンレート：存在×（1 + 接近ブースト）
        float rate = SpawnPerSecond * presence * Mathf.Lerp(1f, 1f + DangerSpawnBoost, _danger01); // ★修正

        // 間引きタイマー（present=0なら自然停止）
        _accum += rate * Time.deltaTime;
        while (_accum >= 1f)
        {
            SpawnOne();
            _accum -= Mathf.Lerp(1f - SpawnJitter, 1f + SpawnJitter, Random.value);
        }

        // オーブ更新（present=0でも寿命で消える）
        float dt = Time.deltaTime;
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].alive) _pool[i].Tick(dt, Cam, _ghostT, this);
        }
    }

    // ====== 生成/プール ======
    void EnsurePrefab()
    {
        if (OrbPrefab) return;

        // 自動生成：白い球＋トレイル（加算Unlit）
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "AutoScreenOrbPrefab";
        Object.Destroy(go.GetComponent<Collider>());

        // トレイルは“消えない”Sprites/Defaultにする（URP Unlitはビルドでピンク化しやすい）
        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.widthCurve = AnimationCurve.EaseInOut(0, 0.06f, 1, 0f);
        var trailMat = new Material(Shader.Find("Sprites/Default"));
        trailMat.SetColor("_Color", new Color(1, 1, 1, 0.5f));
        trail.sharedMaterial = trailMat;
        trail.emitting = true;
        trail.minVertexDistance = 0.02f;

        // 本体メッシュ（使うならSprites/Default。HideCoreMeshなら後で撤去）
        var mr = go.GetComponent<MeshRenderer>();
        if (mr)
        {
            var coreMat = new Material(Shader.Find("Sprites/Default"));
            coreMat.SetColor("_Color", Color.white);
            mr.sharedMaterial = coreMat;
        }

        // 本体メッシュを物理的に外す
        if (HideCoreMesh)
        {
            var mf = go.GetComponent<MeshFilter>();
            var mrComp = go.GetComponent<MeshRenderer>();
            if (mf) Destroy(mf);
            if (mrComp) Destroy(mrComp);
        }

        // テンプレは非表示・非保存・このコンポーネントの子に置く
        go.SetActive(false);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetParent(transform, false);

        OrbPrefab = go;
    }

    void BuildPool()
    {
        // プール親（ヒエラルキー非表示）
        if (_poolRoot == null)
        {
            var root = new GameObject("ScreenOrbPool");
            root.transform.SetParent(transform, false);
            root.hideFlags = HideFlags.HideInHierarchy;
            _poolRoot = root.transform;
        }

        _pool.Clear();
        for (int i = 0; i < PoolSize; i++)
        {
            var o = new Orb();
            o.host = Instantiate(OrbPrefab, _poolRoot);
            o.host.SetActive(false);
            SetRenderable(o.host, false); // 生成直後は確実に非表示

            // 隠しレイヤに入れて絶対映さない
            if (_hiddenLayer >= 0) SetLayerRecursively(o.host, _hiddenLayer);

            _pool.Add(o);
        }
    }

    void SpawnOne()
    {
        // 空きスロット
        Orb o = null;
        for (int i = 0; i < _pool.Count; i++)
        {
            if (!_pool[i].alive) { o = _pool[i]; break; }
        }
        if (o == null) return; // 全部稼働中ならスキップ

        // 画面内ランダム（端すぎない）
        float u = Mathf.Lerp(ViewportMargin.x, 1f - ViewportMargin.x, Random.value);
        float v = Mathf.Lerp(ViewportMargin.y, 1f - ViewportMargin.y, Random.value);

        Vector3 wp = Cam.ViewportToWorldPoint(new Vector3(u, v, PlaneDistance));

        // ---- 進行方向（奥行きを殺してYへ流すオプション対応） ----
        Vector3 right = Cam.transform.right;
        Vector3 up = Cam.transform.up;
        Vector3 dir;

        if (MoveAlongWorldY)
        {
            float sign = RandomizeUpDown ? (Random.value < 0.5f ? -1f : 1f) : 1f;
            dir = Vector3.up * sign;                 // 世界Y
        }
        else if (MoveAlongScreenY)
        {
            float sign = RandomizeUpDown ? (Random.value < 0.5f ? -1f : 1f) : 1f;
            dir = up * sign;                          // 画面の上下だけ
        }
        else
        {
            // 従来挙動：ゴースト方向に軽く流す
            Vector3 forward = Cam.transform.forward;
            dir = forward;
            if (_ghostT)
            {
                Vector3 toG = (_ghostT.position - Cam.transform.position).normalized;
                Vector3 toGFlat = Vector3.ProjectOnPlane(toG, forward).normalized;
                dir = Vector3.Lerp(forward, toGFlat, GravityTowardGhost);
            }
        }

        // 横ズレノイズ（奥行きは入れない）
        dir += right * (Random.value * 2f - 1f) * DriftNoise;
        if (MoveAlongScreenY || MoveAlongWorldY)
        {
            dir += up * Random.Range(-0.2f, 0.2f) * DriftNoise;
        }

        // 画面Y移動時は奥行き成分を完全除去
        if (MoveAlongScreenY && !MoveAlongWorldY)
        {
            Vector3 forward = Cam.transform.forward;
            dir = Vector3.ProjectOnPlane(dir, forward);
        }
        dir.Normalize();
        // ---- ここまで進行方向 ----

        float spd = Random.Range(Speed.x, Speed.y);
        float life = Random.Range(Lifetime.x, Lifetime.y);
        float size = Random.Range(StartSize.x, StartSize.y);

        o.Spawn(wp, dir * spd, life, size, this);
    }

    void OnDestroy()
    {
        // プール親を掃除（ユーザー提供Prefabは触らない）
        if (_poolRoot) Destroy(_poolRoot.gameObject);

        // 自動生成テンプレだけ明示破棄
        if (OrbPrefab && OrbPrefab.hideFlags.HasFlag(HideFlags.HideAndDontSave))
        {
            Destroy(OrbPrefab);
        }
    }

    // 描画On/Offをまとめて切る（プール時の完全OFF用）
    static void SetRenderable(GameObject go, bool enable)
    {
        if (!go) return;

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = enable;
        }

        // TrailRenderer の発生も止めたい場合（Renderer 継承だが念のため）
        var trails = go.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].emitting = enable;
        }
    }

    // Spawn時専用（本体メッシュは常に非表示、トレイルだけ有効化）
    void SetRenderableForSpawn(GameObject go, bool enable)
    {
        if (!go) return;

        // MeshRenderer（残っている場合のみ）。HideCoreMeshなら無効 or そもそも存在しない
        var meshRenderers = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshRenderers.Length; i++)
            meshRenderers[i].enabled = enable && !HideCoreMesh;

        // Trail は表示・非表示を制御
        var trails = go.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].enabled = enable;  // 念のため
            trails[i].emitting = enable;
        }
    }

    //レイヤ再帰設定
    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (!go) return;
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }

    // ====== 内部オーブ ======
    class Orb
    {
        public GameObject host;
        public bool alive;
        float life, maxLife;
        Vector3 vel;
        Vector3 startScale;

        MeshRenderer mr;

        public void Spawn(Vector3 pos, Vector3 velocity, float lifetime, float size, GhostScreenOrbs cfg)
        {
            alive = true;
            if (!mr) mr = host.GetComponent<MeshRenderer>();
            host.transform.position = pos;
            host.transform.rotation = Quaternion.identity;
            startScale = Vector3.one * size;
            host.transform.localScale = startScale;
            vel = velocity;
            life = maxLife = Mathf.Max(0.05f, lifetime);

            // 表示開始：トレイルのみ有効化（本体メッシュは非表示のまま）
            cfg.SetRenderableForSpawn(host, true);
            host.SetActive(true);
            if (cfg._visibleLayer >= 0) GhostScreenOrbs.SetLayerRecursively(host, cfg._visibleLayer);
        }

        public void Tick(float dt, Camera cam, Transform ghost, GhostScreenOrbs cfg)
        {
            if (!alive) return;

            // 進む
            host.transform.position += vel * dt;

            // 常にカメラ前PlaneDistanceの平面へロック（奥行き一定）
            if (cfg.KeepDepthLocked && cam)
            {
                host.transform.position = LockToCameraPlane(cam, host.transform.position, cfg.PlaneDistance);
            }

            // カメラへビルボード
            host.transform.rotation = Quaternion.LookRotation(cam.transform.forward);

            // 経過
            life -= dt;
            float t = 1f - Mathf.Clamp01(life / Mathf.Max(0.0001f, maxLife));

            // スケール/アルファ（本体メッシュは非表示なので主にトレイル見え方）
            float sMul = cfg.ScaleOverLife.Evaluate(t);
            sMul *= Mathf.Lerp(1f, cfg.ScaleByDanger, cfg._danger01); // ★追加
            host.transform.localScale = startScale * sMul;

            // 本体メッシュ用に色を触る処理は残す（存在すれば反映される）
            float a = Mathf.Clamp01(cfg.AlphaOverLife.Evaluate(t));
            a = Mathf.Clamp01(a * (0.95f + 0.05f * cfg._danger01));   // ★追加

            if (!mr) mr = host.GetComponent<MeshRenderer>();
            if (mr && mr.material && mr.material.HasProperty("_Color"))
            {
                var c = mr.material.color;
                c.a = a;
                mr.material.color = c;
            }

            if (life <= 0f)
            {
                // 非表示に戻す：完全OFF + 隠しレイヤへ
                GhostScreenOrbs.SetRenderable(host, false);
                host.SetActive(false);
                if (cfg._hiddenLayer >= 0) GhostScreenOrbs.SetLayerRecursively(host, cfg._hiddenLayer);
                alive = false;
            }
        }

        // カメラ前の一定距離の“平面”へ再投影
        static Vector3 LockToCameraPlane(Camera cam, Vector3 worldPos, float planeDist)
        {
            // 平面の基準点（カメラ位置 + forward * planeDist）
            Vector3 planeOrigin = cam.transform.position + cam.transform.forward * planeDist;
            Vector3 planeNormal = cam.transform.forward; // 法線

            // 現在位置をその平面に正射影
            Vector3 v = worldPos - planeOrigin;
            Vector3 onPlane = v - Vector3.Dot(v, planeNormal) * planeNormal;

            return planeOrigin + onPlane;
        }
    }
}
