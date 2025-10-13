using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    bool GhostSpawn = false;
    public Transform Player;
    public GameObject Ghost;
    public Vector3 GhostPosition;
    public int GhostEncountChance;

    //=== 追加パラメータ（最小限）========================================================
    // スポーン距離（プレイヤーからの最小/最大距離）
    public float MinSpawnDistance = 10f;
    public float MaxSpawnDistance = 50f;
    // 視界内に突然湧かせないための角度（カメラ正面 何度以内は避ける）
    public float MinAngleFromCameraForward = 50f;
    // 視線が壁などで遮られている場所を優先したい場合のレイヤー（任意）
    public LayerMask LineOfSightBlockers;
    // メインカメラ参照（未指定なら自動取得）
    public Camera MainCam;

    //オーディオ系=========================================================================================
    public AudioClip SpawnSE;
    AudioSource audioSource;

    void Start()
    {
        StartCoroutine("Spawn");
        audioSource = GetComponent<AudioSource>();
        if (MainCam == null) MainCam = Camera.main; // 追加：未設定時は自動取得
    }

    private void Update()
    {
        //=== 変更点：符号固定をやめ、プレイヤー周囲のランダム位置を生成 ===============================
        // ・水平方向のランダム（insideUnitCircle）→距離はMin~Maxでランダム
        // ・カメラ正面に近すぎる方向は避ける（フェアネス）
        // ・（任意）プレイヤーとの間に遮蔽物があるとき優先
        Vector3 candidate = Player.position;
        bool decided = false;

        // 過度に重くしないため試行回数に上限
        for (int i = 0; i < 12 && !decided; i++)
        {
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 dir = new Vector3(dir2.x, 0f, dir2.y);

            // カメラ角度チェック（カメラが無い場合はスキップ）
            if (MainCam != null)
            {
                float angle = Vector3.Angle(MainCam.transform.forward, dir);
                if (angle < MinAngleFromCameraForward) continue; // 視界に近すぎる方向は避ける
            }

            float dist = Random.Range(MinSpawnDistance, MaxSpawnDistance);
            candidate = Player.position + dir * dist;

            // 視線遮断（任意）：プレイヤー→候補の間にブロッカーがあるか
            bool blocked = false;
            if (LineOfSightBlockers.value != 0)
            {
                blocked = Physics.Linecast(
                    Player.position + Vector3.up * 1.6f,
                    candidate + Vector3.up * 1.6f,
                    LineOfSightBlockers
                );
            }

            // ブロッカー指定なし→そのまま採用 / 指定あり→遮られてる候補を優先
            if (LineOfSightBlockers.value == 0 || blocked)
            {
                decided = true;
            }
        }

        GhostPosition = decided ? candidate : // 条件を満たす候補が見つかった
                                              // フォールバック：元の仕様に近い形（ただし負号も出る）
            new Vector3(
                Player.transform.position.x + Random.Range(-MaxSpawnDistance, MaxSpawnDistance),
                Player.transform.position.y,
                Player.transform.position.z + Random.Range(-MaxSpawnDistance, MaxSpawnDistance)
            );
    }

    IEnumerator Spawn()
    {
        //抽選が当たるまでは抽選し続ける==========================================================
        while (GhostSpawn == false)
        {
            //=== そのまま：既存の抽選式を維持（調整はしやすいようコメントのみ）===================
            // 0~49 の乱数で 31~49 が当たり → 約38%/回。5秒ごとに抽選。
            GhostEncountChance = Random.Range(0, 50);
            if (GhostEncountChance > 30)
            {
                GhostSpawn = true;
            }
            yield return new WaitForSeconds(5.0f);
        }

        //抽選が当たった時の処理=================================================================
        if (GhostSpawn == true)
        {
            //=== 追加：前兆フック（必要なければ削除OK）=========================================
            // ここでライト点滅・環境音・囁き字幕などを呼ぶとフェアになる
            // e.g., EffectsManager.Instance.Foreshadow(GhostPosition, 2.0f);
            yield return new WaitForSeconds(0.25f); // ほんの少し溜め（微調整可）

            audioSource.PlayOneShot(SpawnSE);
            Instantiate(Ghost, GhostPosition, Quaternion.identity);

            GhostSpawn = false;
            StartCoroutine("Spawn"); // 既存仕様のまま再開
        }
    }
}
