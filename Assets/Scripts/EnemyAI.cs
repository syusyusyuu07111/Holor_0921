using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    bool GhostSpawn = false;                              // 抽選に当たったか
    public Transform Player;                              // プレイヤー
    public GameObject Ghost;                              // 敵プレハブ
    public Vector3 GhostPosition;                         // 次に湧く座標
    public int GhostEncountChance;                        // 抽選値

    // --------------- 生成エリア（座標直指定） ---------------
    public float MinX;                                    // Xの最小値
    public float MaxX;                                    // Xの最大値
    public float MinZ;                                    // Zの最小値
    public float MaxZ;                                    // Zの最大値
    public float SpawnYOffset = 0f;                       // 高さ微調整

    // --------------- 距離と試行回数 ---------------
    public float MinSpawnDistance = 8f;                   // プレイヤーからの最小距離
    public int MaxPickTrials = 16;                        // ランダム試行回数

    // --------------- 生成制御：1体制限＆寿命 ---------------
    public GameObject CurrentGhost;                       // いま存在している幽霊（nullなら不在）
    public float GhostLifetime = 30f;                     // 生成から消滅までの寿命（秒）
    public float RespawnDelayAfterDespawn = 5f;           // 消滅後に抽選を再開するまでの待機（秒）
    public float RetryIntervalWhileAlive = 0.25f;         // 存在中のチェック間隔（軽め）

    // --------------- オーディオ ---------------
    public AudioClip SpawnSE;                             // 生成SE
    AudioSource audioSource;                              // 再生用

    // --------------- 内部フラグ ---------------
    private bool _cooldown;                               // 消滅後の待機中フラグ

    void Start()
    {
        StartCoroutine("Spawn");                          // 生成ループ開始
        audioSource = GetComponent<AudioSource>();        // 取得
    }

    void Update()
    {
        GhostPosition = PickSpawnPointInRect();           // 次の候補を更新
    }

    // --------------- 矩形内ランダム（近すぎは除外） ---------------
    private Vector3 PickSpawnPointInRect()
    {
        Vector3 pick = Player.position;                   // 初期値

        float x0 = Mathf.Min(MinX, MaxX);                 // 正規化した最小X
        float x1 = Mathf.Max(MinX, MaxX);                 // 正規化した最大X
        float z0 = Mathf.Min(MinZ, MaxZ);                 // 正規化した最小Z
        float z1 = Mathf.Max(MinZ, MaxZ);                 // 正規化した最大Z

        for (int i = 0; i < MaxPickTrials; i++)
        {
            float x = Random.Range(x0, x1);               // 矩形内X
            float z = Random.Range(z0, z1);               // 矩形内Z
            pick = new Vector3(x, Player.position.y + SpawnYOffset, z);

            Vector2 d2 = new Vector2(pick.x - Player.position.x, pick.z - Player.position.z);
            if (d2.sqrMagnitude >= MinSpawnDistance * MinSpawnDistance) return pick; // 採用
        }

        // 最遠の隅をフォールバックにする（必ず矩形内）
        Vector3 far = FarthestPointFromPlayerInRect(new Vector2(x0, z0), new Vector2(x1, z1));
        return new Vector3(far.x, Player.position.y + SpawnYOffset, far.z);
    }

    // --------------- 矩形の4隅のうち最遠点 ---------------
    private Vector3 FarthestPointFromPlayerInRect(Vector2 min, Vector2 max)
    {
        Vector2 p = new Vector2(Player.position.x, Player.position.z);
        Vector2[] corners = new Vector2[]
        {
            new Vector2(min.x, min.y),
            new Vector2(min.x, max.y),
            new Vector2(max.x, min.y),
            new Vector2(max.x, max.y)
        };

        float best = -1f; Vector2 bestPt = corners[0];
        for (int i = 0; i < corners.Length; i++)
        {
            float d = (corners[i] - p).sqrMagnitude;      // 2D距離
            if (d > best) { best = d; bestPt = corners[i]; }
        }
        return new Vector3(bestPt.x, 0f, bestPt.y);       // YはあとでSpawnYOffsetを足す
    }

    IEnumerator Spawn()
    {
        while (true)
        {
            // 1体制限：存在中 or クールダウン中は待機 -------------------------------------
            if (CurrentGhost || _cooldown)
            {
                yield return new WaitForSeconds(RetryIntervalWhileAlive);
                continue;
            }

            // 抽選（既存ロジック） -------------------------------------------------------
            GhostEncountChance = Random.Range(0, 50);     // 0〜49
            if (GhostEncountChance > 30) GhostSpawn = true;

            if (GhostSpawn)
            {
                if (!CurrentGhost)                        // 念のため二重ガード
                {
                    if (audioSource && SpawnSE) audioSource.PlayOneShot(SpawnSE);
                    CurrentGhost = Instantiate(Ghost, GhostPosition, Quaternion.identity); // 生成
                    StartCoroutine(GhostLifecycle(CurrentGhost)); // 寿命＆再抽選までの管理
                }
                GhostSpawn = false;                       // 抽選リセット
            }

            yield return new WaitForSeconds(5.0f);        // 次回抽選まで（通常サイクル）
        }
    }

    // --------------- 幽霊の寿命管理：30秒で消滅→5秒後に抽選再開 ---------------
    private IEnumerator GhostLifecycle(GameObject ghost)
    {
        yield return new WaitForSeconds(GhostLifetime);   // 寿命
        if (ghost) Destroy(ghost);                        // 消滅
        if (CurrentGhost == ghost) CurrentGhost = null;   // 参照をクリア（即時）

        _cooldown = true;                                 // クールダウン開始
        yield return new WaitForSeconds(RespawnDelayAfterDespawn); // 5秒待ち
        _cooldown = false;                                // 抽選再開OK
    }

    // --------------- デバッグ可視化 ---------------
    private void OnDrawGizmosSelected()
    {
        float x0 = Mathf.Min(MinX, MaxX);
        float x1 = Mathf.Max(MinX, MaxX);
        float z0 = Mathf.Min(MinZ, MaxZ);
        float z1 = Mathf.Max(MinZ, MaxZ);
        Vector3 center = new Vector3((x0 + x1) * 0.5f, (Player ? Player.position.y : 0f) + SpawnYOffset, (z0 + z1) * 0.5f);
        Vector3 size = new Vector3(Mathf.Abs(x1 - x0), 0.05f, Mathf.Abs(z1 - z0));
        Gizmos.color = Color.yellow; Gizmos.DrawWireCube(center, size);   // 生成範囲
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(GhostPosition, 0.25f); // 候補点
        if (Player) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(Player.position, MinSpawnDistance); } // 最小距離
    }
}
