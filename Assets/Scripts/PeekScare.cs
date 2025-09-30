using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class PeekScare : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip SE; // 単発用（リストが空 or 使い切ったときのフォールバック）

    [Header("SE（複数）")]
    public List<AudioClip> SEList = new List<AudioClip>();
    int seIndex = 0; // 次に鳴らすリスト位置
    // ★変更: 巡回させたいので既定を false に（true だと毎回 0 に戻り、先頭が null だと次の要素だけが毎回選ばれる）
    public bool ResetSEIndexEachPeek = false; // ← 必要なら Inspector で true に戻せます

    public Transform Ghost;
    public Transform Camera;

    public GameObject Goal; // 追う対象（Player）
    public float distance;  // プレイヤーとの距離
    public NavMeshAgent agent;

    [Header("幽霊の動く情報")]
    public float Speed = 5f;
    public float StopDistance = 0.3f;

    // 重複起動防止
    bool running = false;
    Coroutine scareCo;

    // この“覗き”でSEを鳴らしたか（1覗き1回だけ）
    bool playedThisScare = false;

    void Start()
    {
        // AudioSource 自動取得/自動追加（未アタッチでも鳴るように）
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        // 確実に聞こえる初期設定（必要なら後で3D化）
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // 2D（Listenerからの距離に影響されない）
        audioSource.volume = 1f;
        audioSource.mute = false;

        // NavMeshAgent
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!Goal) Goal = GameObject.Find("Player"); // 可能なら Inspector/Tag で渡す

        if (agent)
        {
            agent.speed = Speed;
            agent.stoppingDistance = StopDistance;
            agent.autoBraking = true;
            agent.updateRotation = true;
            agent.isStopped = true; // 初期は停止
        }
    }

    void Update()
    {
        bool peeking = (PeekCamera.Instance != null && PeekCamera.Instance.IsPeeking);

        // 覗き開始 → 追跡開始（1回だけ）
        if (peeking && !running)
        {
            running = true;
            playedThisScare = false; // 覗きごとにリセット

            // ★注意: 巡回させたいなら false のままがオススメ
            if (ResetSEIndexEachPeek)
            {
                seIndex = 0; // 覗くたびに先頭から鳴らしたい特殊ケース
            }

            // 万一インデックスが範囲外なら丸める（保険）
            if (SEList != null && SEList.Count > 0 && (seIndex < 0 || seIndex >= SEList.Count))
            {
                seIndex = Mathf.Clamp(seIndex, 0, SEList.Count - 1);
            }

            // 覗いた“瞬間”に必ず1回だけ再生（距離に依存しない）
            if (!playedThisScare)
            {
                TryPlayOneSEOnce();   // リスト→単発の順に試す
                playedThisScare = true;
            }

            if (agent) agent.isStopped = false;
            scareCo = StartCoroutine(Scare());
        }
        // 覗き終了 → 追跡停止（進行中なら止める）
        else if (!peeking && running)
        {
            if (scareCo != null) StopCoroutine(scareCo);
            if (agent) agent.isStopped = true;
            running = false;
        }
    }

    IEnumerator Scare()
    {
        while (PeekCamera.Instance != null && PeekCamera.Instance.IsPeeking)
        {
            if (Goal != null && agent != null)
            {
                // 追跡先更新
                if (agent.isOnNavMesh)
                {
                    agent.SetDestination(Goal.transform.position);
                }

                // 距離の更新（任意のデバッグ用）
                distance = Vector3.Distance(transform.position, Goal.transform.position);

                // NavMeshAgent基準の到着判定（少しマージン）
                bool arrived = false;
                if (agent.isOnNavMesh)
                {
                    float stop = Mathf.Max(agent.stoppingDistance, StopDistance);
                    if (!agent.pathPending && agent.remainingDistance <= stop + 0.05f)
                    {
                        arrived = true;
                    }
                }

                // 到着時にも鳴らしたい（ただし同じ覗き中は二重再生しない）
                if ((arrived || distance <= StopDistance + 0.1f) && !playedThisScare)
                {
                    agent.isStopped = true;
                    TryPlayOneSEOnce(); // 覗いた瞬間に鳴っていなければ、ここで1回だけ再生
                    playedThisScare = true;
                    break;
                }
            }
            yield return null; // 次フレーム
        }

        running = false;
    }

    // 1回だけ再生：
    // ・SEList を「現在の seIndex から」一周（ラップ）探索して最初の有効クリップ(nullでない)を鳴らす
    // ・鳴らせたら seIndex を「鳴らした要素の次」へ進める（末尾なら 0 に戻す＝巡回）
    // ・有効クリップが1つも無い場合は単発 SE にフォールバック（それも無ければ警告）
    void TryPlayOneSEOnce()
    {
        if (audioSource == null)
        {
            Debug.LogWarning("[SE] AudioSource is null");
            return;
        }

        bool played = false;

        if (SEList != null && SEList.Count > 0)
        {
            int count = SEList.Count;

            // ★ラウンドロビン探索：seIndex から count 回まで試す（全要素1周）
            int start = Mathf.Clamp(seIndex, 0, count - 1);
            for (int n = 0; n < count; n++)
            {
                int idx = (start + n) % count;      // ← ラップして回す
                var clip = SEList[idx];
                if (clip == null) continue;         // null はスキップ

                audioSource.PlayOneShot(clip);
                played = true;

                // ★次回用インデックス：鳴らした要素の「次」へ。末尾の次は 0 に戻す（巡回）
                seIndex = (idx + 1) % count;
                break;
            }

            // 1周しても鳴らせなかった（全部 null）の場合、単発にフォールバック
            if (!played && SE != null)
            {
                audioSource.PlayOneShot(SE);
                played = true;
                // seIndex はそのまま（全部 null なので意味なし）
            }
        }
        else
        {
            // リストが空 → 単発SE
            if (SE != null)
            {
                audioSource.PlayOneShot(SE);
                played = true;
            }
        }

        if (!played)
        {
            // 何も鳴らせなかったときの保険（インスペクタの空要素/未割当を疑う）
            Debug.LogWarning($"[SE] No playable clip. seIndex:{seIndex} listCount:{(SEList != null ? SEList.Count : 0)} singleNull:{SE == null}");
            // この覗きでは以降試さないように抑制
            playedThisScare = true;
        }
    }
}
