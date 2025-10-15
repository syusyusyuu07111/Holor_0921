using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class EnemyAI : MonoBehaviour
{
    // ===== 基本 =====
    [Header("基本")]
    public Transform Player;
    public GameObject Ghost;                      // 生成プレハブ
    public Vector3 GhostPosition;                 // 次に湧く座標（Updateで更新）
    public int GhostEncountChance;                // 抽選値（ログ用）

    // ===== 範囲 =====
    [Header("範囲")]
    public float MinX, MaxX, MinZ, MaxZ;
    public float SpawnYOffset = 0f;

    // ===== 距離/試行 =====
    [Header("距離/試行")]
    public float MinSpawnDistance = 8f;
    public int MaxPickTrials = 16;

    // ===== 制御 =====
    [Header("制御")]
    public GameObject CurrentGhost;               // いまの幽霊
    public float GhostLifetime = 30f;
    public float RespawnDelayAfterDespawn = 5f;
    public float RetryIntervalWhileAlive = 0.25f;
    private bool _cooldown;

    // ===== 通知 =====
    [Header("通知（SE/VFX）")]
    public AudioClip SpawnSE;
    public float SpawnSEVolume = 1.0f;
    public Vector2 SpawnSEPitchRange = new Vector2(0.98f, 1.02f);
    public bool UsePlayClipAtPoint = true;
    public GameObject SpawnVfxPrefab;
    public float SpawnVfxLifetime = 2f;
    private AudioSource audioSource;

    // ===== イベント（湧いた瞬間） =====
    [Header("イベント")]
    public UnityEvent OnGhostSpawned = new UnityEvent();

    // ===== 抽選制御（外部操作） =====
    [Header("抽選制御")]
    public bool AutoStart = false;                // チュートリアル中はfalse推奨
    private Coroutine _spawnLoop;
    public bool IsSpawning => _spawnLoop != null;

    // ===== 最初=1、2回目=2 のためのカウンタ =====
    private int _spawnCount = 0;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (AutoStart) _spawnLoop = StartCoroutine(SpawnLoop());
    }

    void Update()
    {
        GhostPosition = PickSpawnPointInRect();
    }

    // === 外部公開：開始/停止 ===
    public void BeginSpawning()
    {
        if (_spawnLoop == null) _spawnLoop = StartCoroutine(SpawnLoop());
    }

    public void StopSpawning()
    {
        if (_spawnLoop != null) { StopCoroutine(_spawnLoop); _spawnLoop = null; }
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            // 1体制限／クールダウン中は待機
            if (CurrentGhost || _cooldown)
            {
                yield return new WaitForSeconds(RetryIntervalWhileAlive);
                continue;
            }

            // 抽選
            GhostEncountChance = Random.Range(0, 50);   // 0〜49
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
                CurrentGhost = Instantiate(Ghost, GhostPosition, Quaternion.identity);

                // ★ ここで SearchChase を見つけて状態を強制（1回目=1、2回目=2）
                var sc = CurrentGhost.GetComponentInChildren<SearchChase>(true);
                if (sc)
                {
                    _spawnCount++;
                    if (_spawnCount == 1) sc.ForceState(1);
                    else if (_spawnCount == 2) sc.ForceState(2);
                    // 3回目以降は何もしない → SearchChase 側のランダムが効く
                }

                // Tutorial等へ通知（このイベントを使ってStep3/4など進行）
                OnGhostSpawned?.Invoke();

                // SE/VFX
                PlaySpawnSoundAt(GhostPosition);
                if (SpawnVfxPrefab)
                {
                    var vfx = Instantiate(SpawnVfxPrefab, GhostPosition, Quaternion.identity);
                    if (SpawnVfxLifetime > 0f) Destroy(vfx, SpawnVfxLifetime);
                }

                // 寿命管理
                StartCoroutine(GhostLifecycle(CurrentGhost));
            }

            yield return new WaitForSeconds(5f); // 次の抽選まで
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

    private void PlaySpawnSoundAt(Vector3 pos)
    {
        if (!SpawnSE) return;
        float pitch = Mathf.Clamp(Random.Range(SpawnSEPitchRange.x, SpawnSEPitchRange.y), 0.5f, 2f);

        if (UsePlayClipAtPoint)
        {
            AudioSource.PlayClipAtPoint(SpawnSE, pos, Mathf.Clamp01(SpawnSEVolume));
        }
        else
        {
            if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
            audioSource.transform.position = pos;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(SpawnSE, Mathf.Clamp01(SpawnSEVolume));
        }
    }

    // ===== スポーン位置の算出 =====
    private Vector3 PickSpawnPointInRect()
    {
        if (!Player)
        {
            // Player 未割り当てでも落ちないよう中央へ
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
            if (d2.sqrMagnitude >= MinSpawnDistance * MinSpawnDistance) return pick;
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

    private void OnDrawGizmosSelected()
    {
        float x0 = Mathf.Min(MinX, MaxX);
        float x1 = Mathf.Max(MinX, MaxX);
        float z0 = Mathf.Min(MinZ, MaxZ);
        float z1 = Mathf.Max(MinZ, MaxZ);
        Vector3 center = new Vector3((x0 + x1) * 0.5f, (Player ? Player.position.y : 0f) + SpawnYOffset, (z0 + z1) * 0.5f);
        Vector3 size = new Vector3(Mathf.Abs(x1 - x0), 0.05f, Mathf.Abs(z1 - z0));
        Gizmos.color = Color.yellow; Gizmos.DrawWireCube(center, size);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(GhostPosition, 0.25f);
        if (Player) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(Player.position, MinSpawnDistance); }
    }
}
