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

    // ====== スポーン範囲（XZ矩形） ======
    [Header("スポーン範囲（XZ矩形）")]
    public float MinX, MaxX, MinZ, MaxZ;
    public float SpawnYOffset = 0f;

    // ====== 距離/試行 ======
    [Header("距離/試行")]
    public float MinSpawnDistance = 8f;
    public int MaxPickTrials = 16;

    // ====== 生成制御 ======
    [Header("生成制御")]
    public GameObject CurrentGhost;          // 1体制限
    public float GhostLifetime = 30f;
    public float RespawnDelayAfterDespawn = 5f;
    public float RetryIntervalWhileAlive = 0.25f;
    private bool _cooldown;

    // ====== 登場SE ======
    [Header("登場SE")]
    public AudioSource AudioSource;          // インスペクタでアタッチ
    public AudioClip AudioClip;              // インスペクタでアタッチ（AudioSource.clip にも入れてOK）

    // ====== イベント：湧いた瞬間 ======
    [Header("イベント")]
    public UnityEvent OnGhostSpawned = new UnityEvent();

    // ====== 抽選制御（外部操作） ======
    [Header("抽選制御")]
    public bool AutoStart = false;
    private Coroutine _spawnLoop;
    public bool IsSpawning => _spawnLoop != null;

    // ====== 1回目=STATE1 / 2回目=STATE2 を保証 ======
    private static int s_GlobalSpawnCount = 0;
    [Tooltip("Play開始時に1→2カウンタをリセット（通常はtrue）")]
    public bool ResetCounterOnStart = true;

    // ====== 最初の抽選は必ずスポーン ======
    [Header("最初の抽選保証")]
    public bool GuaranteeFirstRoll = true;
    private bool _firstRollDone = false;

    // ---- 小さな保険（Editorでミュートされがちな時用） ----
    [Header("デバッグ/保険")]
    [Tooltip("Start時に AudioListener.pause を解除する")]
    public bool ForceUnpauseAudioListener = true;

    // ================= ライフサイクル =================

    void Start()
    {
        if (ResetCounterOnStart) s_GlobalSpawnCount = 0;

        if (AudioSource == null) AudioSource = GetComponent<AudioSource>();

        // Inspectorで指定したClipをAudioSource.clipにも同期（見落とし防止）
        if (AudioSource && AudioClip && AudioSource.clip != AudioClip)
            AudioSource.clip = AudioClip;

        if (ForceUnpauseAudioListener) AudioListener.pause = false;

        // 参考ログ
#if UNITY_2023_1_OR_NEWER
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
        var listeners = Object.FindObjectsOfType<AudioListener>();
#endif
        Debug.Log($"[EnemyAI] Listeners={(listeners?.Length ?? 0)}, " +
                  $"ASrc={(AudioSource ? "OK" : "null")}, " +
                  $"Clip={(AudioClip ? AudioClip.name : "null")}, " +
                  $"ASrc.Mute={(AudioSource ? AudioSource.mute : false)}, " +
                  $"ASrc.Vol={(AudioSource ? AudioSource.volume : 0f):0.00}, " +
                  $"Listener.pause={AudioListener.pause}");

        if (AutoStart) _spawnLoop = StartCoroutine(SpawnLoop());
    }

    void OnValidate()
    {
        // 編集中に差し替えたときも同期
        if (AudioSource && AudioClip) AudioSource.clip = AudioClip;
    }

    void Update()
    {
        GhostPosition = PickSpawnPointInRect();
    }

    // ================= 外部公開：開始/停止 =================

    public void BeginSpawning()
    {
        if (_spawnLoop == null)
        {
            _firstRollDone = false;
            _spawnLoop = StartCoroutine(SpawnLoop());
        }
    }

    public void StopSpawning()
    {
        if (_spawnLoop != null) { StopCoroutine(_spawnLoop); _spawnLoop = null; }
    }

    // ================= スポーン処理 =================

    // 即時に1体だけ確定スポーン
    public bool SpawnOnceImmediate()
    {
        if (CurrentGhost || _cooldown || !Ghost) return false;

        var pos = PickSpawnPointInRect();
        CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);

        ForceFirstTwoStates(CurrentGhost);
        OnGhostSpawned?.Invoke();

        TryPlaySpawnSE();    // ★ 生成直後にSE

        StartCoroutine(GhostLifecycle(CurrentGhost));
        _firstRollDone = true;
        return true;
    }

    // 抽選ループ
    IEnumerator SpawnLoop()
    {
        while (true)
        {
            if (CurrentGhost || _cooldown)
            {
                yield return new WaitForSeconds(RetryIntervalWhileAlive);
                continue;
            }

            // 最初の抽選は必ずスポーン
            if (GuaranteeFirstRoll && !_firstRollDone)
            {
                _firstRollDone = true;

                if (!Ghost) { Debug.LogWarning("[EnemyAI] Ghost prefab 未設定。"); yield return new WaitForSeconds(5f); continue; }

                var pos0 = PickSpawnPointInRect();
                CurrentGhost = Instantiate(Ghost, pos0, Quaternion.identity);

                ForceFirstTwoStates(CurrentGhost);
                OnGhostSpawned?.Invoke();

                TryPlaySpawnSE();    // ★ ここでもSE

                StartCoroutine(GhostLifecycle(CurrentGhost));
                yield return new WaitForSeconds(5f);
                continue;
            }

            // 通常抽選
            GhostEncountChance = Random.Range(0, 50);
            bool spawn = (GhostEncountChance > 30);
            _firstRollDone = true;

            if (spawn && !CurrentGhost)
            {
                if (!Ghost) { Debug.LogWarning("[EnemyAI] Ghost prefab 未設定。"); yield return new WaitForSeconds(5f); continue; }

                var pos = PickSpawnPointInRect();
                CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);

                ForceFirstTwoStates(CurrentGhost);
                OnGhostSpawned?.Invoke();

                TryPlaySpawnSE();    // ★ ここでもSE

                StartCoroutine(GhostLifecycle(CurrentGhost));
            }

            yield return new WaitForSeconds(5f);
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

    // ================= STATE固定（1回目=1、2回目=2） =================
    private void ForceFirstTwoStates(GameObject ghostRoot)
    {
        if (!ghostRoot) return;

        var chasers = ghostRoot.GetComponentsInChildren<SearchChase>(true);
        if (chasers == null || chasers.Length == 0) { s_GlobalSpawnCount++; return; }

        int forced =
            (s_GlobalSpawnCount == 0) ? 1 :
            (s_GlobalSpawnCount == 1) ? 2 : 0;

        if (forced != 0)
            foreach (var sc in chasers) sc.ForceState(forced);

        s_GlobalSpawnCount++;
    }

    // ================= SE再生（開始/終了ログ付き） =================

    // 実際の再生処理
    private void TryPlaySpawnSE()
    {
        // 他所でポーズされてても鳴るよう一応解除（任意）
        if (ForceUnpauseAudioListener && AudioListener.pause) AudioListener.pause = false;

        if (AudioSource != null && AudioClip != null)
        {
            // 再生開始ログ
            Debug.Log($"[EnemyAI] SE start: {AudioClip.name}  " +
                      $"vol={AudioSource.volume:0.00}, pitch={AudioSource.pitch:0.00}, " +
                      $"listener.pause={AudioListener.pause}, src.mute={AudioSource.mute}");

            AudioSource.PlayOneShot(AudioClip);

            // ピッチを考慮して長さを算出 → 実時間で終了ログ
            float dur = AudioClip.length / Mathf.Max(0.01f, Mathf.Abs(AudioSource.pitch));
            StartCoroutine(LogSEndAfter(dur, AudioClip.name));
        }
        else
        {
            Debug.LogWarning("[EnemyAI] SE 再生不可：AudioSource か AudioClip が未設定です。");
        }
    }

    // 再生終了ログ用（timeScale=0でも確実に動く）
    private IEnumerator LogSEndAfter(float seconds, string clipName)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, seconds));
        Debug.Log($"[EnemyAI] SE end: {clipName}");
    }

    // ================= スポーン地点選定 =================

    private Vector3 PickSpawnPointInRect()
    {
        if (!Player)
        {
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
}
