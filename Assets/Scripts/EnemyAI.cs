using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class EnemyAI : MonoBehaviour
{
    // ====== 基本 ======
    [Header("基本")]
    public Transform Player;
    public GameObject Ghost;                 // 生成する本体プレハブ
    public Vector3 GhostPosition;            // 次に湧く予定の座標（Updateで更新）
    [Tooltip("直近の抽選値（デバッグ用）")]
    public int GhostEncountChance;

    // ====== スポーン範囲（XZ矩形） ======
    [Header("スポーン範囲（XZ矩形）")]
    public float MinX, MaxX, MinZ, MaxZ;
    public float SpawnYOffset = 0f;

    // ★ 2つ目の部屋にいるときのスポーン制限用
    [Header("2つ目の部屋用スポーン制限")]
    [Tooltip("プレイヤーが2部屋目に入ったかを教えてくれるスクリプト")]
    public SecondRoomTutorial secondRoomTutorial;

    [Tooltip("2部屋目の入口ドア（X座標を境界として使う）")]
    public Transform doorBorderX;

    [Tooltip("2部屋目にいるとき、ドアのX座標からどれだけ右側に寄せて湧かせるか")]
    public float secondRoomDoorOffsetX = 0.5f;

    // ====== 距離/試行 ======
    [Header("距離/試行")]
    public float MinSpawnDistance = 8f;
    public int MaxPickTrials = 16;

    // ====== 生成制御 ======
    [Header("生成制御")]
    public GameObject CurrentGhost;          // いま生きてるやつ（1体制限）
    public float GhostLifetime = 30f;        // ゴーストの寿命(秒)
    public float RespawnDelayAfterDespawn = 5f;   // 死んでから次の抽選を再開するまでの待ち
    public float RetryIntervalWhileAlive = 0.25f; // すでに湧いてる間のポーリング間隔
    private bool _cooldown;

    // ====== 登場SE ======
    [Header("登場SE")]
    [Tooltip("CRIのAudioManager。幽霊出現SEをここから鳴らす")]
    public AudioManager AudioMgr;

    // ====== 出現エフェクト ======
    [Header("出現エフェクト")]
    [Tooltip("ゴースト出現時に同じ場所で生成するVFXプレハブ")]
    public GameObject SpawnEffectPrefab;

    [Tooltip("trueならエフェクトをゴーストの子にする（追従させたい時）")]
    public bool AttachEffectToGhost = true;

    [Tooltip("エフェクトを自動で消す秒数。0以下なら放置")]
    public float EffectAutoDestroy = 2f;

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

    // ---- 小さな保険（Editorでミュートされがちな時用）----
    [Header("デバッグ/保険")]
    [Tooltip("Start時に AudioListener.pause を解除する")]
    public bool ForceUnpauseAudioListener = true;

    // ====== 画面演出（青化） ======
    [Header("画面演出（青化）")]
    [Tooltip("ColorAdjustments入りのVolume（URP）")]
    public Volume PostVolume;

    [Range(0f, 1f)]
    public float BlueTintStrength = 0.6f;

    [Tooltip("青化の目標色（白→この色へ補間）")]
    public Color BlueTintColor = new Color(0.70f, 0.85f, 1.0f, 1.0f);   // ★ 4引数に修正済み

    [Tooltip("フェード時間（出現→青化）")]
    public float BlueFadeIn = 0.20f;

    [Tooltip("フェード時間（消滅→元に戻す）")]
    public float BlueFadeOut = 0.25f;

    [Tooltip("近接警告（GameOver）の危険度を参照して青演出を抑制する")]
    public GameOver DangerRef;

    private ColorAdjustments _ca;
    private Color _baseFilter = Color.white;
    private float _blueLerp = 0f;
    private float _baseVolumeWeight = 1f;

    // =================サイクル =================

    void Start()
    {
        if (ResetCounterOnStart) s_GlobalSpawnCount = 0;

        if (ForceUnpauseAudioListener) AudioListener.pause = false;

#if UNITY_2023_1_OR_NEWER
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
        var listeners = Object.FindObjectsOfType<AudioListener>();
#endif
        Debug.Log($"[EnemyAI] Start. Listeners={(listeners?.Length ?? 0)}, " +
                  $"Listener.pause={AudioListener.pause}, " +
                  $"AudioMgr={(AudioMgr ? "OK" : "null")}");

        // ColorAdjustmentsの参照を取る＆元の色を記録
        if (PostVolume && PostVolume.profile)
        {
            PostVolume.profile.TryGet(out _ca);
            if (_ca != null) _baseFilter = _ca.colorFilter.value;
            _baseVolumeWeight = PostVolume.weight;
        }

        if (AutoStart) _spawnLoop = StartCoroutine(SpawnLoop());
    }

    void Update()
    {
        GhostPosition = PickSpawnPointInRect();

        // 出現している間だけ青化（フェードでON/OFF）
        if (_ca != null)
        {
            bool present = (CurrentGhost != null);
            float target = present ? 1f : 0f;
            float speed = present ? (1f / Mathf.Max(0.01f, BlueFadeIn))
                                   : (1f / Mathf.Max(0.01f, BlueFadeOut));
            _blueLerp = Mathf.MoveTowards(_blueLerp, target, Time.deltaTime * speed);

            float danger = (DangerRef != null) ? DangerRef.GetDangerBlend01() : 0f;
            float blueWeight = _blueLerp * (1f - danger);

            Color goal = Color.Lerp(_baseFilter, BlueTintColor, Mathf.Clamp01(BlueTintStrength));
            _ca.colorFilter.value = Color.Lerp(_baseFilter, goal, blueWeight);

            if (PostVolume)
            {
                PostVolume.weight = Mathf.Lerp(_baseVolumeWeight, 1f, blueWeight);
            }
        }
    }

    // ================= 外部 =================

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
        if (_spawnLoop != null)
        {
            StopCoroutine(_spawnLoop);
            _spawnLoop = null;
        }
    }

    // ================= スポーン処理 =================

    public bool SpawnOnceImmediate()
    {
        if (CurrentGhost || _cooldown || !Ghost) return false;

        var pos = PickSpawnPointInRect();
        LogSpawnPosition("[EnemyAI] SpawnOnceImmediate", pos);

        CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);
        SpawnGhostEffect(pos, CurrentGhost);

        ForceFirstTwoStates(CurrentGhost);
        OnGhostSpawned?.Invoke();

        TryPlaySpawnSE();

        StartCoroutine(GhostLifecycle(CurrentGhost));
        _firstRollDone = true;
        return true;
    }

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

                if (!Ghost)
                {
                    Debug.LogWarning("[EnemyAI] Ghost prefab 未設定。");
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                var pos0 = PickSpawnPointInRect();
                LogSpawnPosition("[EnemyAI] FirstRoll Spawn", pos0);

                CurrentGhost = Instantiate(Ghost, pos0, Quaternion.identity);
                SpawnGhostEffect(pos0, CurrentGhost);

                ForceFirstTwoStates(CurrentGhost);
                OnGhostSpawned?.Invoke();

                TryPlaySpawnSE();

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
                if (!Ghost)
                {
                    Debug.LogWarning("[EnemyAI] Ghost prefab 未設定。");
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                var pos = PickSpawnPointInRect();
                LogSpawnPosition("[EnemyAI] Random Spawn", pos);

                CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);
                SpawnGhostEffect(pos, CurrentGhost);

                ForceFirstTwoStates(CurrentGhost);
                OnGhostSpawned?.Invoke();

                TryPlaySpawnSE();

                StartCoroutine(GhostLifecycle(CurrentGhost));
            }

            yield return new WaitForSeconds(5f);
        }
    }

    private void LogSpawnPosition(string prefix, Vector3 pos)
    {
        bool inSecond = (secondRoomTutorial != null && secondRoomTutorial.IsPlayerInSecondRoom);
        float doorX = (doorBorderX != null) ? doorBorderX.position.x : 0f;
        float playerX = Player ? Player.position.x : 0f;

        string doorInfo = doorBorderX
            ? $"doorX={doorX:F2}"
            : "doorX=(未設定)";

        Debug.Log($"{prefix}: spawnPos=({pos.x:F2},{pos.y:F2},{pos.z:F2}), " +
                  $"playerX={playerX:F2}, secondRoom={inSecond}, {doorInfo}");
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

    // ================= 出現エフェクト生成 =================
    private void SpawnGhostEffect(Vector3 spawnPos, GameObject ghostInstance)
    {
        if (!SpawnEffectPrefab) return;

        Transform parent = null;
        if (AttachEffectToGhost && ghostInstance)
        {
            parent = ghostInstance.transform;
        }

        GameObject fx = Instantiate(
            SpawnEffectPrefab,
            spawnPos,
            Quaternion.identity,
            parent
        );

        if (EffectAutoDestroy > 0f)
        {
            Destroy(fx, EffectAutoDestroy);
        }
    }

    // ================= STATE固定（1回目=1、2回目=2） =================
    private void ForceFirstTwoStates(GameObject ghostRoot)
    {
        if (!ghostRoot) return;

        var chasers = ghostRoot.GetComponentsInChildren<SearchChase>(true);
        if (chasers == null || chasers.Length == 0)
        {
            s_GlobalSpawnCount++;
            return;
        }

        int forced =
            (s_GlobalSpawnCount == 0) ? 1 :
            (s_GlobalSpawnCount == 1) ? 2 : 0;

        if (forced != 0)
        {
            foreach (var sc in chasers)
            {
                sc.ForceState(forced);
            }
        }

        s_GlobalSpawnCount++;
    }

    // ================= SE再生（CRI版） =================
    private void TryPlaySpawnSE()
    {
        if (ForceUnpauseAudioListener && AudioListener.pause)
            AudioListener.pause = false;

        if (AudioMgr != null)
        {
            Debug.Log("[EnemyAI] Ghost spawn -> Play SE (AudioManager.GHOSTAPPEAR)");
            AudioMgr.GHOSTAPPEAR();
        }
        else
        {
            Debug.LogWarning("[EnemyAI] AudioMgr が割り当てられていないので幽霊SEは鳴りません。");
        }
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

        bool inSecond = (secondRoomTutorial != null && secondRoomTutorial.IsPlayerInSecondRoom);

        if (inSecond && doorBorderX)
        {
            float doorX = doorBorderX.position.x;
            float beforeX0 = x0;
            float beforeX1 = x1;

            // X 下限は "ドア位置" でクランプ
            x0 = Mathf.Max(x0, doorX);

            if (x0 > x1)
            {
                x1 = x0;
                Debug.LogWarning(
                    $"[EnemyAI] ドアX={doorX:F2} がスポーン範囲外。X範囲 ({beforeX0:F2},{beforeX1:F2}) → ({x0:F2},{x1:F2}) に補正"
                );
            }
            else
            {
                Debug.Log(
                    $"[EnemyAI] 2部屋目スポーン調整: doorX={doorX:F2}, X範囲=({beforeX0:F2},{beforeX1:F2})→({x0:F2},{x1:F2})"
                );
            }
        }

        for (int i = 0; i < MaxPickTrials; i++)
        {
            float x = Random.Range(x0, x1);
            float z = Random.Range(z0, z1);
            pick = new Vector3(x, Player.position.y + SpawnYOffset, z);

            // ★ 2部屋目にいるときは「ドア＋オフセット」より右側に必ず寄せる
            if (inSecond && doorBorderX)
            {
                float doorX = doorBorderX.position.x;
                float minXFromDoor = doorX + secondRoomDoorOffsetX;
                if (pick.x < minXFromDoor)
                {
                    pick.x = minXFromDoor;
                }
            }

            Vector2 d2 = new Vector2(
                pick.x - Player.position.x,
                pick.z - Player.position.z
            );
            if (d2.sqrMagnitude >= MinSpawnDistance * MinSpawnDistance)
            {
                return pick;
            }
        }

        // どうしても条件に合わない時は矩形内の一番遠い隅
        Vector3 far = FarthestPointFromPlayerInRect(
            new Vector2(x0, z0),
            new Vector2(x1, z1)
        );

        if (inSecond && doorBorderX)
        {
            float doorX = doorBorderX.position.x;
            float minXFromDoor = doorX + secondRoomDoorOffsetX;
            if (far.x < minXFromDoor)
                far.x = minXFromDoor;
        }

        return new Vector3(far.x, Player.position.y + SpawnYOffset, far.z);
    }

    private Vector3 FarthestPointFromPlayerInRect(Vector2 min, Vector2 max)
    {
        Vector2 p = Player
            ? new Vector2(Player.position.x, Player.position.z)
            : Vector2.zero;

        Vector2[] corners =
        {
            new Vector2(min.x, min.y),
            new Vector2(min.x, max.y),
            new Vector2(max.x, min.y),
            new Vector2(max.x, max.y)
        };

        float best = -1f;
        Vector2 bestPt = corners[0];

        for (int i = 0; i < corners.Length; i++)
        {
            float d = (corners[i] - p).sqrMagnitude;
            if (d > best)
            {
                best = d;
                bestPt = corners[i];
            }
        }

        return new Vector3(bestPt.x, 0f, bestPt.y);
    }
}
