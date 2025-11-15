using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;                 // ★追加
using UnityEngine.Rendering.Universal;       // ★追加

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
    public GameObject SpawnEffectPrefab;   // ★追加: 出現エフェクト

    [Tooltip("trueならエフェクトをゴーストの子にする（追従させたい時）")]
    public bool AttachEffectToGhost = true; // ★追加

    [Tooltip("エフェクトを自動で消す秒数。0以下なら放置")]
    public float EffectAutoDestroy = 2f;    // ★追加

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

    // ====== 画面演出（青化） ======
    [Header("画面演出（青化）")]
    [Tooltip("ColorAdjustments入りのVolume（URP）")]
    public Volume PostVolume;                     // ★追加

    [Range(0f, 1f)]
    public float BlueTintStrength = 0.6f;         // ★追加: どれだけ青くするか
    [Tooltip("青化の目標色（白→この色へ補間）")]
    public Color BlueTintColor = new Color(0.70f, 0.85f, 1.0f, 1.0f); // ★追加

    [Tooltip("フェード時間（出現→青化）")]
    public float BlueFadeIn = 0.20f;              // ★追加
    [Tooltip("フェード時間（消滅→元に戻す）")]
    public float BlueFadeOut = 0.25f;             // ★追加

    [Tooltip("近接警告（GameOver）の危険度を参照して青演出を抑制する")]
    public GameOver DangerRef;                    // ★追加

    private ColorAdjustments _ca;                 // ★追加（内部参照）
    private Color _baseFilter = Color.white;      // ★追加（開始時のColorFilterを記録）
    private float _blueLerp = 0f;                 // ★追加（0..1）
    private float _baseVolumeWeight = 1f;         // ★追加

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
        Debug.Log($"[EnemyAI] Listeners={(listeners?.Length ?? 0)}, " +
                  $"Listener.pause={AudioListener.pause}, " +
                  $"AudioMgr={(AudioMgr ? "OK" : "null")}");

        // ColorAdjustmentsの参照を取る＆元の色を記録
        if (PostVolume && PostVolume.profile)
        {
            PostVolume.profile.TryGet(out _ca);
            if (_ca != null) _baseFilter = _ca.colorFilter.value;
            _baseVolumeWeight = PostVolume.weight;  // 元のWeightを記録
        }

        if (AutoStart) _spawnLoop = StartCoroutine(SpawnLoop());
    }



    void Update()
    {
        GhostPosition = PickSpawnPointInRect();

        // 出現している間だけ青化（フェードでON/OFF）
        if (_ca != null)
        {
            bool present = (CurrentGhost != null);                   // いま出現中？
            float target = present ? 1f : 0f;
            float speed = present ? (1f / Mathf.Max(0.01f, BlueFadeIn))
                                   : (1f / Mathf.Max(0.01f, BlueFadeOut));
            _blueLerp = Mathf.MoveTowards(_blueLerp, target, Time.deltaTime * speed);

            // 近接警告が強いほど青を抑える（優先度：警告ビネット）
            float danger = (DangerRef != null) ? DangerRef.GetDangerBlend01() : 0f;   // 0..1
            float blueWeight = _blueLerp * (1f - danger); // 危険時ほど小さく

            // ベース→青色へ。BlueTintStrengthで上限、blueWeightで在位＋優先度を適用
            Color goal = Color.Lerp(_baseFilter, BlueTintColor, Mathf.Clamp01(BlueTintStrength));
            _ca.colorFilter.value = Color.Lerp(_baseFilter, goal, blueWeight);

            // Volume自体のWeight
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

    // 即時に1体だけ確定スポーン
    public bool SpawnOnceImmediate()
    {
        if (CurrentGhost || _cooldown || !Ghost) return false;

        var pos = PickSpawnPointInRect();

        // ゴースト本体生成
        CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);

        // 生成エフェクト
        SpawnGhostEffect(pos, CurrentGhost);

        ForceFirstTwoStates(CurrentGhost);
        OnGhostSpawned?.Invoke();

        TryPlaySpawnSE();    // SE鳴らす

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
                // すでに誰かいる間は短いポーリング
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

                CurrentGhost = Instantiate(Ghost, pos0, Quaternion.identity);

                // 生成エフェクト
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

                CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);

                // 生成エフェクト
                SpawnGhostEffect(pos, CurrentGhost);

                ForceFirstTwoStates(CurrentGhost);
                OnGhostSpawned?.Invoke();

                TryPlaySpawnSE();

                StartCoroutine(GhostLifecycle(CurrentGhost));
            }

            yield return new WaitForSeconds(5f);
        }
    }

    private IEnumerator GhostLifecycle(GameObject ghost)
    {
        // 一定時間生きる
        yield return new WaitForSeconds(GhostLifetime);

        // 消す
        if (ghost) Destroy(ghost);
        if (CurrentGhost == ghost) CurrentGhost = null;

        // クールダウンを挟んで再抽選許可
        _cooldown = true;
        yield return new WaitForSeconds(RespawnDelayAfterDespawn);
        _cooldown = false;
    }

    // ================= 出現エフェクト生成 =================
    // ゴースト生成直後に呼ばれる
    private void SpawnGhostEffect(Vector3 spawnPos, GameObject ghostInstance)
    {
        if (!SpawnEffectPrefab) return; // エフェクト未指定なら何もしない

        // どの親につける？
        Transform parent = null;
        if (AttachEffectToGhost && ghostInstance)
        {
            parent = ghostInstance.transform;
        }

        // 生成
        GameObject fx = Instantiate(
            SpawnEffectPrefab,
            spawnPos,
            Quaternion.identity,
            parent
        );

        // 子にした場合、足元にピタッと置きたいとかあればここでローカル補正もできる
        // いまはそのまま。

        // 一定時間後に消す
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
        // 他所でポーズされてても鳴るよう一応解除（任意）
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
            // プレイヤー不明なら矩形の中心あたり
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

        // 一定距離以上離れた点をランダムに試す
        for (int i = 0; i < MaxPickTrials; i++)
        {
            float x = Random.Range(x0, x1);
            float z = Random.Range(z0, z1);
            pick = new Vector3(x, Player.position.y + SpawnYOffset, z);

            Vector2 d2 = new Vector2(
                pick.x - Player.position.x,
                pick.z - Player.position.z
            );
            if (d2.sqrMagnitude >= MinSpawnDistance * MinSpawnDistance)
            {
                return pick; // 採用
            }
        }

        // どうしても取れなかったら、矩形の四隅のうち一番遠いところ
        Vector3 far = FarthestPointFromPlayerInRect(
            new Vector2(x0, z0),
            new Vector2(x1, z1)
        );
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
