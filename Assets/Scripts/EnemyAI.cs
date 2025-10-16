using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class EnemyAI : MonoBehaviour
{
    // ====== 基本 ======
    [Header("基本")]
    public Transform Player;
    public GameObject Ghost;                 // 生成プレハブ
    public Vector3 GhostPosition;            // 次に湧く座標（Updateで更新）
    [Tooltip("直近の抽選値（デバッグ用）")]
    public int GhostEncountChance;

    // ====== スポーン範囲 ======
    [Header("スポーン範囲（XZ矩形）")]
    public float MinX, MaxX, MinZ, MaxZ;
    public float SpawnYOffset = 0f;          // 高さ補正（地面がY=0でない時など）

    // ====== 距離/試行 ======
    [Header("距離/試行")]
    public float MinSpawnDistance = 8f;      // プレイヤーから最低でもこの距離
    public int MaxPickTrials = 16;           // ランダム試行回数

    // ====== 生成制御 ======
    [Header("生成制御")]
    public GameObject CurrentGhost;          // 現在出ている個体（1体制限）
    public float GhostLifetime = 30f;        // 出現から自壊まで
    public float RespawnDelayAfterDespawn = 5f;
    public float RetryIntervalWhileAlive = 0.25f;
    private bool _cooldown;

    // ====== 登場演出（SE/VFX） ======
    [Header("登場SE")]
    public AudioClip SpawnSE;
    [Range(0f, 1f)] public float SpawnSEVolume = 1.0f;
    public Vector2 SpawnSEPitchRange = new Vector2(0.98f, 1.02f);

    [Tooltip("trueで必ず2D再生（距離減衰なし）")]
    public bool Force2D = false;

    [Tooltip("3D再生時の最小距離（これ以内は減衰しない）")]
    public float SE_MinDistance = 2f;

    [Tooltip("3D再生時の最大距離（これ以遠は聞こえない）")]
    public float SE_MaxDistance = 35f;

    public AudioRolloffMode SE_Rolloff = AudioRolloffMode.Linear;

    [Tooltip("距離や再生成否をデバッグ出力")]
    public bool LogSpawnSE = false;

    [Header("登場VFX（任意）")]
    public GameObject SpawnVfxPrefab;
    public float SpawnVfxLifetime = 2f;

    private AudioSource _sharedSource;       // 2D再生やフォールバックに使用

    // ====== イベント：湧いた瞬間 ======
    [Header("イベント")]
    public UnityEvent OnGhostSpawned = new UnityEvent();

    // ====== 抽選制御（外部操作） ======
    [Header("抽選制御")]
    public bool AutoStart = false;           // Tutorial中はfalse推奨
    private Coroutine _spawnLoop;
    public bool IsSpawning => _spawnLoop != null;

    // =========================================================

    void Start()
    {
        _sharedSource = GetComponent<AudioSource>();
        if (AutoStart) _spawnLoop = StartCoroutine(SpawnLoop());
    }

    void Update()
    {
        GhostPosition = PickSpawnPointInRect(); // 候補は常時更新OK
    }

    // ---- 外部公開：開始/停止 ----
    public void BeginSpawning()
    {
        if (_spawnLoop == null) _spawnLoop = StartCoroutine(SpawnLoop());
    }

    public void StopSpawning()
    {
        if (_spawnLoop != null) { StopCoroutine(_spawnLoop); _spawnLoop = null; }
    }

    /// <summary>
    /// 即時に1体だけ確定スポーンさせる（SE/VFXあり）。
    /// 条件を満たさない（既に存在、クールダウン中、プレハブ未設定）なら false。
    /// </summary>
    public bool SpawnOnceImmediate()
    {
        if (CurrentGhost || _cooldown || !Ghost) return false;

        // いまの候補を安全に再計算してから使う
        var pos = PickSpawnPointInRect();

        CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);

        // イベント通知（Tutorialなどが受け取る）
        OnGhostSpawned?.Invoke();

        // 演出
        PlaySpawnSoundAt(pos);
        if (SpawnVfxPrefab)
        {
            var vfx = Instantiate(SpawnVfxPrefab, pos, Quaternion.identity);
            if (SpawnVfxLifetime > 0f) Destroy(vfx, SpawnVfxLifetime);
        }

        // 寿命管理
        StartCoroutine(GhostLifecycle(CurrentGhost));
        return true;
    }

    // ---- メインの抽選ループ ----
    IEnumerator SpawnLoop()
    {
        while (true)
        {
            // 1体制限 & クールダウン
            if (CurrentGhost || _cooldown)
            {
                yield return new WaitForSeconds(RetryIntervalWhileAlive);
                continue;
            }

            // 抽選
            GhostEncountChance = Random.Range(0, 50); // 0〜49
            bool spawn = (GhostEncountChance > 30);

            if (spawn && !CurrentGhost)
            {
                if (!Ghost)
                {
                    Debug.LogWarning("[EnemyAI] Ghost prefab 未設定。生成スキップ。");
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                // 生成
                var pos = PickSpawnPointInRect();
                CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);

                // イベント通知（Tutorialなどが受け取る）
                OnGhostSpawned?.Invoke();

                // 演出
                PlaySpawnSoundAt(pos);
                if (SpawnVfxPrefab)
                {
                    var vfx = Instantiate(SpawnVfxPrefab, pos, Quaternion.identity);
                    if (SpawnVfxLifetime > 0f) Destroy(vfx, SpawnVfxLifetime);
                }

                // 寿命管理
                StartCoroutine(GhostLifecycle(CurrentGhost));
            }

            yield return new WaitForSeconds(5f); // 次回抽選まで
        }
    }

    private IEnumerator GhostLifecycle(GameObject ghost)
    {
        yield return new WaitForSeconds(GhostLifetime);
        if (ghost) Destroy(ghost);
        if (CurrentGhost == ghost) CurrentGhost = null;

        _cooldown = true;
        yield return new WaitForSeconds(RespawnDelayAfterDespawn);
        _cooldown = false;
    }

    // ---- SE再生：2D/3D 切替 & 3Dパラメータ明示 ----
    private void PlaySpawnSoundAt(Vector3 pos)
    {
        if (!SpawnSE) return;

        if (LogSpawnSE && Camera.main)
        {
            float d = Vector3.Distance(Camera.main.transform.position, pos);
            Debug.Log($"[EnemyAI] SpawnSE: distance={d:F1}m 2D={Force2D}");
        }

        float pitch = Mathf.Clamp(Random.Range(SpawnSEPitchRange.x, SpawnSEPitchRange.y), 0.5f, 2f);

        if (Force2D)
        {
            // 2D（距離減衰なし）で確実に
            if (!_sharedSource) _sharedSource = gameObject.AddComponent<AudioSource>();
            _sharedSource.spatialBlend = 0f; // 2D
            _sharedSource.pitch = pitch;
            _sharedSource.PlayOneShot(SpawnSE, Mathf.Clamp01(SpawnSEVolume));
            return;
        }

        // 一時的な3D AudioSourceを作って明示設定
        GameObject go = new GameObject("SpawnSE_AudioTemp");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = SpawnSE;
        src.spatialBlend = 1f;                         // 3D
        src.rolloffMode = SE_Rolloff;
        src.minDistance = Mathf.Max(0.01f, SE_MinDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.01f, SE_MaxDistance);
        src.dopplerLevel = 0f;
        src.spread = 0f;
        src.priority = 128;
        src.pitch = pitch;
        src.volume = Mathf.Clamp01(SpawnSEVolume);

        src.Play();
        Destroy(go, SpawnSE.length / Mathf.Max(0.01f, src.pitch) + 0.1f);
    }

    // ---- スポーン地点選定 ----
    private Vector3 PickSpawnPointInRect()
    {
        if (!Player)
        {
            // Player未設定でも落ちないフォールバック
            return new Vector3(
                Mathf.Lerp(MinX, MaxX, 0.5f),
                SpawnYOffset,
                Mathf.Lerp(MinZ, MaxZ, 0.5f)
            );
        }

        Vector3 pick = Player.position;

        float x0 = Mathf.Min(MinX, MaxX);
        float x1 = Mathf.Max(MinX, MaxX);
        float z0 = Mathf.Min(MinZ, MaxZ);
        float z1 = Mathf.Max(MinZ, MaxZ);

        for (int i = 0; i < MaxPickTrials; i++)
        {
            float x = Random.Range(x0, x1);
            float z = Random.Range(z0, z1);
            pick = new Vector3(x, Player.position.y + SpawnYOffset, z);

            Vector2 d2 = new Vector2(pick.x - Player.position.x, pick.z - Player.position.z);
            if (d2.sqrMagnitude >= MinSpawnDistance * MinSpawnDistance)
                return pick; // 採用
        }

        // フォールバック：最遠角
        Vector3 far = FarthestPointFromPlayerInRect(new Vector2(x0, z0), new Vector2(x1, z1));
        return new Vector3(far.x, Player.position.y + SpawnYOffset, far.z);
    }

    private Vector3 FarthestPointFromPlayerInRect(Vector2 min, Vector2 max)
    {
        Vector2 p = Player ? new Vector2(Player.position.x, Player.position.z) : Vector2.zero;
        Vector2[] corners =
        {
            new Vector2(min.x, min.y),
            new Vector2(min.x, max.y),
            new Vector2(max.x, min.y),
            new Vector2(max.x, max.y)
        };

        float best = -1f; Vector2 bestPt = corners[0];
        for (int i = 0; i < corners.Length; i++)
        {
            float d = (corners[i] - p).sqrMagnitude;
            if (d > best) { best = d; bestPt = corners[i]; }
        }
        return new Vector3(bestPt.x, 0f, bestPt.y);
    }

    // ---- デバッグ可視化 ----
    private void OnDrawGizmosSelected()
    {
        float x0 = Mathf.Min(MinX, MaxX);
        float x1 = Mathf.Max(MinX, MaxX);
        float z0 = Mathf.Min(MinZ, MaxZ);
        float z1 = Mathf.Max(MinZ, MaxZ);

        Vector3 center = new Vector3((x0 + x1) * 0.5f,
                                     (Player ? Player.position.y : 0f) + SpawnYOffset,
                                     (z0 + z1) * 0.5f);
        Vector3 size = new Vector3(Mathf.Abs(x1 - x0), 0.05f, Mathf.Abs(z1 - z0));

        Gizmos.color = Color.yellow; Gizmos.DrawWireCube(center, size);         // 生成範囲
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(GhostPosition, 0.25f); // 候補点
        if (Player)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(Player.position, MinSpawnDistance);           // 最小距離
        }
    }
}
