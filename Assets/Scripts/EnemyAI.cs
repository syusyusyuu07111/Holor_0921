using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class EnemyAI : MonoBehaviour
{
    //======================================================================
    // 基本参照（プレイヤー / 幽霊プレハブ / デバッグ値）
    //======================================================================
    [Header("基本")]
    public Transform Player;                    // 距離判定やスポーン座標の基準にするプレイヤー
    public GameObject Ghost;                    // 生成する幽霊プレハブ
    public Vector3 GhostPosition;               // 「次に湧く座標」のデバッグ表示用（Updateで更新）
    [Tooltip("直近の抽選値（デバッグ用）")]
    public int GhostEncountChance;              // 抽選に使った乱数の記録（Inspectorで確認用）

    //======================================================================
    // スポーン範囲（XZ平面の矩形）
    //======================================================================
    [Header("スポーン範囲（XZ矩形）")]
    public float MinX, MaxX, MinZ, MaxZ;        // スポーン候補範囲（XZの四角）
    public float SpawnYOffset = 0f;             // 高さ方向オフセット（床から浮かせたい時など）

    //======================================================================
    // 2つ目の部屋用：スポーン制限（ドアのXより右側に寄せる）
    //======================================================================
    [Header("2つ目の部屋用スポーン制限")]
    [Tooltip("プレイヤーが2部屋目に入ったかを教えてくれるスクリプト")]
    public SecondRoomTutorial secondRoomTutorial;

    [Tooltip("2部屋目の入口ドア（X座標を境界として使う）")]
    public Transform doorBorderX;

    [Tooltip("2部屋目にいるとき、ドアのX座標からどれだけ右側に寄せて湧かせるか")]
    public float secondRoomDoorOffsetX = 0.5f;

    //======================================================================
    // 距離/試行
    //======================================================================
    [Header("距離/試行")]
    public float MinSpawnDistance = 8f;         // プレイヤーから最低どれだけ離して湧かせるか
    public int MaxPickTrials = 16;              // 乱数で候補点を試す回数（これを超えたらフォールバック）

    //======================================================================
    // 生成制御（1体制限・寿命・クールダウン）
    //======================================================================
    [Header("生成制御")]
    public GameObject CurrentGhost;             // 現在生存中の幽霊（1体制限）
    public float GhostLifetime = 30f;           // 湧いた幽霊が消えるまでの秒数
    public float RespawnDelayAfterDespawn = 5f; // 消えてから次の抽選を再開するまでの待ち
    public float RetryIntervalWhileAlive = 0.25f;// すでに湧いてる間の「監視」間隔
    private bool _cooldown;                     // trueの間は抽選を止める（クールダウン中）

    //======================================================================
    // 出現SE（CRI）
    //======================================================================
    [Header("登場SE")]
    [Tooltip("CRIのAudioManager。幽霊出現SEをここから鳴らす")]
    public AudioManager AudioMgr;

    //======================================================================
    // 出現エフェクト（幽霊と同じ位置でVFXを出す）
    //======================================================================
    [Header("出現エフェクト")]
    [Tooltip("ゴースト出現時に同じ場所で生成するVFXプレハブ")]
    public GameObject SpawnEffectPrefab;

    [Tooltip("trueならエフェクトをゴーストの子にする（追従させたい時）")]
    public bool AttachEffectToGhost = true;

    [Tooltip("エフェクトを自動で消す秒数。0以下なら放置")]
    public float EffectAutoDestroy = 2f;

    //======================================================================
    // イベント（湧いた瞬間に外部へ通知）
    //======================================================================
    [Header("イベント")]
    public UnityEvent OnGhostSpawned = new UnityEvent();

    //======================================================================
    // 抽選ループ制御（外部から開始/停止）
    //======================================================================
    [Header("抽選制御")]
    public bool AutoStart = false;              // trueならStartで自動開始
    private Coroutine _spawnLoop;               // 生成ループのコルーチン
    public bool IsSpawning => _spawnLoop != null;

    //======================================================================
    // 1体目=STATE1 / 2体目=STATE2 を保証（3体目以降はランダム）
    // ※ static なので「シーン内で1個だけ置く」前提だと分かりやすい
    //======================================================================
    private static int s_GlobalSpawnCount = 0;
    [Tooltip("Play開始時に1→2カウンタをリセット（通常はtrue）")]
    public bool ResetCounterOnStart = true;

    //======================================================================
    // 最初の抽選は必ずスポーン（ただし2部屋目では無効にする仕様）
    //======================================================================
    [Header("最初の抽選保証")]
    public bool GuaranteeFirstRoll = true;
    private bool _firstRollDone = false;

    //======================================================================
    // デバッグ/保険（EditorでAudioがミュートされてる時の対策）
    //======================================================================
    [Header("デバッグ/保険")]
    [Tooltip("Start時に AudioListener.pause を解除する")]
    public bool ForceUnpauseAudioListener = true;

    //======================================================================
    // 画面演出：青化（URP VolumeのColorAdjustmentsをいじる）
    //======================================================================
    [Header("画面演出（青化）")]
    [Tooltip("ColorAdjustments入りのVolume（URP）")]
    public Volume PostVolume;

    [Range(0f, 1f)]
    public float BlueTintStrength = 0.6f;       // 「どれくらい青に寄せるか」

    [Tooltip("青化の目標色（白→この色へ補間）")]
    public Color BlueTintColor = new Color(0.70f, 0.85f, 1.0f, 1.0f);

    [Tooltip("フェード時間（出現→青化）")]
    public float BlueFadeIn = 0.20f;

    [Tooltip("フェード時間（消滅→元に戻す）")]
    public float BlueFadeOut = 0.25f;

    [Tooltip("近接警告（GameOver）の危険度を参照して青演出を抑制する")]
    public GameOver DangerRef;

    // Blue演出内部状態
    private ColorAdjustments _ca;               // VolumeのColorAdjustments参照
    private Color _baseFilter = Color.white;    // 元のcolorFilter（後で戻す用）
    private float _blueLerp = 0f;               // 0→1で青演出の強さを管理
    private float _baseVolumeWeight = 1f;       // 元のVolume.weight

    //======================================================================
    // Unity Lifecycle
    //======================================================================

    void Start()
    {
        // Play開始時にスポーン回数をリセットしたい場合
        if (ResetCounterOnStart) s_GlobalSpawnCount = 0;

        // EditorでAudioListener.pauseがtrueになってると音が鳴らないので解除（任意）
        if (ForceUnpauseAudioListener) AudioListener.pause = false;

        // デバッグ用：AudioListenerが存在するか確認
#if UNITY_2023_1_OR_NEWER
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
        var listeners = Object.FindObjectsOfType<AudioListener>();
#endif
        Debug.Log($"[EnemyAI] Start. Listeners={(listeners?.Length ?? 0)}, " +
                  $"Listener.pause={AudioListener.pause}, " +
                  $"AudioMgr={(AudioMgr ? "OK" : "null")}");

        // VolumeからColorAdjustmentsを取り、元の色を保存（あとで戻すため）
        if (PostVolume && PostVolume.profile)
        {
            PostVolume.profile.TryGet(out _ca);
            if (_ca != null) _baseFilter = _ca.colorFilter.value;
            _baseVolumeWeight = PostVolume.weight;
        }

        // 自動開始する設定ならスポーンループ開始
        if (AutoStart) _spawnLoop = StartCoroutine(SpawnLoop());
    }

    void Update()
    {
        // Inspectorで「次に湧く座標」を確認できるように、毎フレーム計算しておく
        GhostPosition = PickSpawnPointInRect();

        //========================
        // 出現中だけ青化（フェードでON/OFF）
        //========================
        if (_ca != null)
        {
            // 幽霊がいる間は1へ、いないなら0へ向かう
            bool present = (CurrentGhost != null);
            float target = present ? 1f : 0f;

            // フェードイン/アウトの速度を「秒数→1に到達する速度」に変換
            float speed = present ? (1f / Mathf.Max(0.01f, BlueFadeIn))
                                  : (1f / Mathf.Max(0.01f, BlueFadeOut));

            _blueLerp = Mathf.MoveTowards(_blueLerp, target, Time.deltaTime * speed);

            // GameOver側の危険度（赤ビネットなど）と演出が喧嘩しないように抑制
            float danger = (DangerRef != null) ? DangerRef.GetDangerBlend01() : 0f;
            float blueWeight = _blueLerp * (1f - danger);

            // 元の色→青目標色へ補間（Strengthは「青目標色の濃さ」）
            Color goal = Color.Lerp(_baseFilter, BlueTintColor, Mathf.Clamp01(BlueTintStrength));
            _ca.colorFilter.value = Color.Lerp(_baseFilter, goal, blueWeight);

            // Volume.weight も少し持ち上げて、演出が見えるようにする
            if (PostVolume)
            {
                PostVolume.weight = Mathf.Lerp(_baseVolumeWeight, 1f, blueWeight);
            }
        }
    }

    //======================================================================
    // 外部API（他スクリプトから開始/停止）
    //======================================================================

    public void BeginSpawning()
    {
        // 既に走っているなら二重起動しない
        if (_spawnLoop == null)
        {
            _firstRollDone = false;                 // 先頭保証をリセット
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

    //======================================================================
    // 1回だけ即スポーン（デバッグ/イベント用）
    //======================================================================

    public bool SpawnOnceImmediate()
    {
        // すでにいる / クールダウン中 / プレハブ未設定なら何もしない
        if (CurrentGhost || _cooldown || !Ghost) return false;

        // 今この瞬間のスポーン位置を決めて生成
        var pos = PickSpawnPointInRect();
        LogSpawnPosition("[EnemyAI] SpawnOnceImmediate", pos);

        CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);

        // VFXがあれば同じ場所に生成（必要ならゴーストの子にする）
        SpawnGhostEffect(pos, CurrentGhost);

        // 1体目/2体目のSTATE固定
        ForceFirstTwoStates(CurrentGhost);

        // 外部に「湧いた」を通知
        OnGhostSpawned?.Invoke();

        // 出現SE
        TryPlaySpawnSE();

        // 寿命管理
        StartCoroutine(GhostLifecycle(CurrentGhost));

        _firstRollDone = true;
        return true;
    }

    //======================================================================
    // スポーンループ（一定間隔で抽選し、条件を満たしたら湧かせる）
    //======================================================================

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            // すでに存在 or クールダウン中 → 少し待って再チェック（負荷を下げる）
            if (CurrentGhost || _cooldown)
            {
                yield return new WaitForSeconds(RetryIntervalWhileAlive);
                continue;
            }

            // プレイヤーが2部屋目にいるか
            bool inSecond =
                (secondRoomTutorial != null && secondRoomTutorial.IsPlayerInSecondRoom);

            // 抽選間隔（ここを変えると出現頻度が変わる）
            float rollInterval = 5f;

            //============================================================
            // 1回目保証：ただし「2部屋目では保証しない」仕様
            //============================================================
            if (GuaranteeFirstRoll && !_firstRollDone && !inSecond)
            {
                _firstRollDone = true;

                if (!Ghost)
                {
                    Debug.LogWarning("[EnemyAI] Ghost prefab 未設定。");
                    yield return new WaitForSeconds(rollInterval);
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
                yield return new WaitForSeconds(rollInterval);
                continue;
            }

            // 「通常抽選に入った時点で、1回目扱いは消費した」扱いにする
            _firstRollDone = true;

            //============================================================
            // 通常抽選（部屋によって確率とルールが変わる）
            //============================================================
            if (inSecond)
            {
                // 2部屋目：ミッション状態で確率を変える
                bool whisperActive =
                    (secondRoomTutorial != null && secondRoomTutorial.IsGhostWhisperMissionActive);

                GhostEncountChance = Random.Range(0, 100); // 0〜99

                // ミッション中は出現率アップ
                bool spawn = whisperActive
                    ? (GhostEncountChance > 40)
                    : (GhostEncountChance > 90);

                if (spawn && !CurrentGhost)
                {
                    if (!Ghost)
                    {
                        Debug.LogWarning("[EnemyAI] Ghost prefab 未設定。");
                        yield return new WaitForSeconds(rollInterval);
                        continue;
                    }

                    var pos = PickSpawnPointInRect();
                    LogSpawnPosition("[EnemyAI] Random Spawn (SecondRoom)", pos);

                    CurrentGhost = Instantiate(Ghost, pos, Quaternion.identity);
                    SpawnGhostEffect(pos, CurrentGhost);

                    ForceFirstTwoStates(CurrentGhost);
                    OnGhostSpawned?.Invoke();

                    TryPlaySpawnSE();

                    StartCoroutine(GhostLifecycle(CurrentGhost));
                }
            }
            else
            {
                // 1部屋目：従来の確率ロジック
                GhostEncountChance = Random.Range(0, 50);
                bool spawn = (GhostEncountChance > 30); // 約38%

                if (spawn && !CurrentGhost)
                {
                    if (!Ghost)
                    {
                        Debug.LogWarning("[EnemyAI] Ghost prefab 未設定。");
                        yield return new WaitForSeconds(rollInterval);
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
            }

            // 次の抽選まで待つ
            yield return new WaitForSeconds(rollInterval);
        }
    }

    //======================================================================
    // デバッグログ：スポーン座標が「期待通り制限されているか」を確認する
    //======================================================================

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

    //======================================================================
    // 寿命管理：一定時間後に消し、少し待ってから抽選を再開する
    //======================================================================

    private IEnumerator GhostLifecycle(GameObject ghost)
    {
        yield return new WaitForSeconds(GhostLifetime);

        if (ghost) Destroy(ghost);
        if (CurrentGhost == ghost) CurrentGhost = null;

        // 「すぐ次が湧く」と怖さが薄れるのでクールダウンを入れる
        _cooldown = true;
        yield return new WaitForSeconds(RespawnDelayAfterDespawn);
        _cooldown = false;
    }

    //======================================================================
    // 出現エフェクト：スポーン地点にVFXを出す
    //======================================================================

    private void SpawnGhostEffect(Vector3 spawnPos, GameObject ghostInstance)
    {
        if (!SpawnEffectPrefab) return;

        // 追従させたい場合は親をゴーストにする
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

        // 一定時間で自動破棄（ループしないエフェクト向け）
        if (EffectAutoDestroy > 0f)
        {
            Destroy(fx, EffectAutoDestroy);
        }
    }

    //======================================================================
    // STATE固定：1体目=1 / 2体目=2 / 3体目以降は {1,2} ランダム
    //======================================================================

    private void ForceFirstTwoStates(GameObject ghostRoot)
    {
        if (!ghostRoot) return;

        var chasers = ghostRoot.GetComponentsInChildren<SearchChase>(true);

        // SearchChaseが無いプレハブでも「回数カウントだけ進める」
        if (chasers == null || chasers.Length == 0)
        {
            s_GlobalSpawnCount++;
            return;
        }

        int forced;

        if (s_GlobalSpawnCount == 0)
        {
            forced = 1; // 1体目は必ず STATE1
        }
        else if (s_GlobalSpawnCount == 1)
        {
            forced = 2; // 2体目は必ず STATE2
        }
        else
        {
            // int版 Random.Range(min, max) は max が含まれない → (1,3) なら {1,2}
            forced = Random.Range(1, 3);
        }

        // 子を含めたSearchChase全員に「このSTATEにしろ」を命令
        foreach (var sc in chasers)
        {
            sc.ForceState(forced);
        }

        Debug.Log($"[EnemyAI] ForceFirstTwoStates: globalSpawn={s_GlobalSpawnCount}, forcedState={forced}");
        s_GlobalSpawnCount++;
    }

    //======================================================================
    // 出現SE：AudioListener.pause の状態も保険で見て鳴らす
    //======================================================================

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

    //======================================================================
    // スポーン地点選定：矩形から「プレイヤーから一定距離以上」離れた点を探す
    // ・2部屋目の場合はドアXより右側に寄せる
    // ・見つからない時は「一番遠い隅」にフォールバック
    //======================================================================

    private Vector3 PickSpawnPointInRect()
    {
        // Playerが未設定なら、とりあえず矩形の中央付近を返す（落ちないための保険）
        if (!Player)
        {
            return new Vector3(
                Mathf.Lerp(MinX, MaxX, 0.5f),
                SpawnYOffset,
                Mathf.Lerp(MinZ, MaxZ, 0.5f)
            );
        }

        Vector3 pick = Player.position;

        // Min/Maxが逆に入っていても動くように正規化
        float x0 = Mathf.Min(MinX, MaxX);
        float x1 = Mathf.Max(MinX, MaxX);
        float z0 = Mathf.Min(MinZ, MaxZ);
        float z1 = Mathf.Max(MinZ, MaxZ);

        bool inSecond = (secondRoomTutorial != null && secondRoomTutorial.IsPlayerInSecondRoom);

        //============================================================
        // 2部屋目にいる時：ドアXを境界に「右側だけ」湧くようにする
        //============================================================
        if (inSecond && doorBorderX)
        {
            float doorX = doorBorderX.position.x;
            float beforeX0 = x0;
            float beforeX1 = x1;

            // Xの下限をドアXでクランプする（左側に湧かせない）
            x0 = Mathf.Max(x0, doorX);

            // ドアが範囲外で、結果として下限が上限を超えた場合の保険
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

        //============================================================
        // 乱数で候補点を作り、プレイヤーからの距離条件を満たすものを返す
        //============================================================
        for (int i = 0; i < MaxPickTrials; i++)
        {
            float x = Random.Range(x0, x1);
            float z = Random.Range(z0, z1);
            pick = new Vector3(x, Player.position.y + SpawnYOffset, z);

            // 2部屋目：ドア＋オフセットより右側に押し込む
            if (inSecond && doorBorderX)
            {
                float doorX = doorBorderX.position.x;
                float minXFromDoor = doorX + secondRoomDoorOffsetX;
                if (pick.x < minXFromDoor)
                {
                    pick.x = minXFromDoor;
                }
            }

            // 距離判定はXZだけで見る（Yの段差を無視して安定させる）
            Vector2 d2 = new Vector2(
                pick.x - Player.position.x,
                pick.z - Player.position.z
            );

            if (d2.sqrMagnitude >= MinSpawnDistance * MinSpawnDistance)
            {
                return pick;
            }
        }

        //============================================================
        // 条件に合う点が見つからない場合：
        // 矩形の4隅のうち、プレイヤーから最も遠い点を返す（フォールバック）
        //============================================================
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

    //======================================================================
    // 矩形の4隅のうち、プレイヤーから最も遠い点を返す
    // ※ PickSpawnPoint が失敗した時の「安全な保険」
    //======================================================================

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