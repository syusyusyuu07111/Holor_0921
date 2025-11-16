using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(10)]
public class GhostScreenOrbs : MonoBehaviour
{
    // ===== 参照 =====
    [Header("参照")]
    [Tooltip("幽霊が出ているかを見る EnemyAI（CurrentGhost を参照）")]
    public EnemyAI Enemy;

    [Tooltip("エフェクトを出すカメラ。未設定なら Camera.main")]
    public Camera Cam;

    [Tooltip("カメラ前に出したい“ゆらゆらパーティクル”のPrefab")]
    public GameObject OrbPrefab;

    [Tooltip("接近度(GetDangerBlend01)を読む参照。なくてもOK")]
    public GameOver DangerRef;

    // ===== スポーン頻度 =====
    [Header("スポーン設定")]
    [Tooltip("ゴースト出現中の 1秒あたりのスポーン数の基準")]
    public float SpawnPerSecond = 15f;   // ★「もっとたくさん」ならここを上げる

    [Tooltip("スポーン間隔のバラつき（1に近いほどランダム）")]
    [Range(0f, 1f)]
    public float SpawnJitter = 0.3f;

    // ===== 位置 =====
    [Header("位置")]
    [Tooltip("カメラからどれだけ前に出すか（m）")]
    public float PlaneDistance = 0.5f;

    // ===== 寿命/スケール =====
    [Header("寿命/スケール")]
    [Tooltip("オーブ1つの寿命範囲（秒）")]
    public Vector2 Lifetime = new Vector2(0.8f, 1.4f);

    [Tooltip("寿命に応じたスケール変化（0=生まれた瞬間,1=消える直前）")]
    public AnimationCurve ScaleOverLife = AnimationCurve.EaseInOut(0, 1, 1, 0.7f);

    [Tooltip("接近時にスケールへかかる倍率（1.15 なら +15%）")]
    public float ScaleByDanger = 1.15f;

    // ===== 強度（距離/接近） =====
    [Header("出現中の強度")]
    [Tooltip("ゴーストが出ているときの最低出現強度（0〜1）")]
    [Range(0, 1)]
    public float MinPresence = 0.6f;

    [Tooltip("この半径以内で近いほど出現量が増える")]
    public float DistanceBoostRadius = 8f;

    [Header("接近ブースト")]
    [Tooltip("接近度によってスポーンレートに足す倍率（1.0 → +100%）")]
    public float DangerSpawnBoost = 1.0f;

    // ===== 深度ロック =====
    [Header("深度ロック")]
    [Tooltip("trueなら常にカメラ前 PlaneDistance の位置にロックする")]
    public bool KeepInFrontOfCamera = true;

    // ===== プール =====
    [Header("プール")]
    [Tooltip("同時に存在しうるオーブの最大数")]
    public int PoolSize = 48;

    // ---- 内部 ----
    private float _accum;
    private readonly List<Orb> _pool = new List<Orb>();
    private Transform _poolRoot;
    private Transform _ghostT;
    private bool _initialized = false;

    [HideInInspector] public float _danger01 = 0f;

    // ================= ライフサイクル =================

    void Awake()
    {
        if (!Cam) Cam = Camera.main;
    }

    void Start()
    {
        if (!OrbPrefab)
        {
            Debug.LogWarning("[GhostScreenOrbs] OrbPrefab が設定されていません。");
            return;
        }

        BuildPool();
        _initialized = true;
    }

    void OnEnable()
    {
        // 再有効化時はプールを全部リセット
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i] != null && _pool[i].host)
            {
                _pool[i].host.SetActive(false);
                _pool[i].alive = false;
            }
        }
    }

    void Update()
    {
        if (!_initialized || !Cam || !Enemy) return;

        // ゴーストが出ているか
        bool present = (Enemy.CurrentGhost != null);
        _ghostT = present ? Enemy.CurrentGhost.transform : null;

        // 出現中の強度（距離で増減）
        float presence = present ? 1f : 0f;
        if (presence > 0f && _ghostT)
        {
            float d = Vector3.Distance(Cam.transform.position, _ghostT.position);
            float w = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, DistanceBoostRadius));
            presence = Mathf.Max(MinPresence, w);
        }

        // 接近度（0..1）
        _danger01 = (DangerRef ? Mathf.Clamp01(DangerRef.GetDangerBlend01()) : 0f);

        // スポーンレート：存在×（1 + 接近ブースト）
        float rate = SpawnPerSecond * presence * Mathf.Lerp(1f, 1f + DangerSpawnBoost, _danger01);

        // 間引きタイマー
        _accum += rate * Time.deltaTime;
        while (_accum >= 1f)
        {
            SpawnOne();
            _accum -= Mathf.Lerp(1f - SpawnJitter, 1f + SpawnJitter, Random.value);
        }

        // オーブ更新
        float dt = Time.deltaTime;
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].alive) _pool[i].Tick(dt, Cam, this);
        }
    }

    // ================= プール =================

    void BuildPool()
    {
        if (!_poolRoot)
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
            o.alive = false;
            _pool.Add(o);
        }
    }

    // ================= スポーン =================

    void SpawnOne()
    {
        // 空きスロットを探す
        Orb o = null;
        for (int i = 0; i < _pool.Count; i++)
        {
            if (!_pool[i].alive)
            {
                o = _pool[i];
                break;
            }
        }
        if (o == null) return; // 全部使用中ならスキップ

        if (!Cam) return;

        // ★ カメラの正面 & 中心に出す
        //   カメラ位置 + forward * PlaneDistance
        Vector3 spawnPos = Cam.transform.position + Cam.transform.forward * PlaneDistance;

        float life = Random.Range(Lifetime.x, Lifetime.y);
        float size = 1f;    // 見た目のサイズはパーティクルPrefab側に任せる

        o.Spawn(spawnPos, life, size, this, Cam);
    }

    void OnDestroy()
    {
        if (_poolRoot) Destroy(_poolRoot.gameObject);
    }

    // ================= 内部クラス：オーブ =================

    class Orb
    {
        public GameObject host;
        public bool alive;
        float life, maxLife;
        Vector3 startScale;

        public void Spawn(Vector3 pos, float lifetime, float size, GhostScreenOrbs cfg, Camera cam)
        {
            alive = true;

            if (!host) return;

            // 生成位置：カメラの正面
            host.transform.position = pos;
            host.transform.rotation = Quaternion.LookRotation(cam.transform.forward);

            startScale = Vector3.one * size;
            host.transform.localScale = startScale;

            life = maxLife = Mathf.Max(0.05f, lifetime);

            // 有効化
            host.SetActive(true);

            // パーティクルをリスタート（揺れなどはPrefabに任せる）
            var particles = host.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].Clear(true);
                particles[i].Play(true);
            }
        }

        public void Tick(float dt, Camera cam, GhostScreenOrbs cfg)
        {
            if (!alive || !host) return;

            // カメラの前にロックしておきたい場合
            if (cfg.KeepInFrontOfCamera && cam)
            {
                Vector3 centerPos = cam.transform.position + cam.transform.forward * cfg.PlaneDistance;
                host.transform.position = centerPos;
                host.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
            }

            // 寿命進行
            life -= dt;
            float t = 1f - Mathf.Clamp01(life / Mathf.Max(0.0001f, maxLife));

            // スケール変化（必要なければ ScaleOverLife をフラットにするだけでOK）
            float sMul = cfg.ScaleOverLife.Evaluate(t);
            sMul *= Mathf.Lerp(1f, cfg.ScaleByDanger, cfg._danger01);
            host.transform.localScale = startScale * sMul;

            // 寿命切れで消す
            if (life <= 0f)
            {
                host.SetActive(false);
                alive = false;
            }
        }
    }
}
