using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 覗き中(IsPeeking=true)の間だけゴーストを追跡させ、SEを1回だけ再生する。
/// ※ 必ず Inspector で「対応する PeekCamera」を割り当ててください（自動検索しません）
/// </summary>
public class PeekScare : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private PeekCamera peekCamera;  // ← 対応する PeekCamera を割り当て
    [SerializeField] private GameObject goal;        // 追う対象（通常はプレイヤー）
    [SerializeField] private NavMeshAgent agent;     // ゴーストに付けた Agent
    [SerializeField] private AudioSource audioSource;

    [Header("移動設定")]
    [SerializeField] private float speed = 5f;
    [SerializeField] private float stopDistance = 0.3f;

    [Header("SE 再生設定")]
    [Tooltip("リストが空 or 使えない時に鳴らす単発SE（フォールバック）")]
    [SerializeField] private AudioClip fallbackSE;

    [Tooltip("複数候補。null要素はスキップ。巡回再生します。")]
    [SerializeField] private List<AudioClip> seList = new List<AudioClip>();

    [Tooltip("覗くたびにインデックスを0に戻したい特殊ケースだけON。通常はOFF推奨。")]
    [SerializeField] private bool resetSEIndexEachPeek = false;

    private int seIndex = 0;           // 次に鳴らすリスト位置
    private bool running = false;      // 追跡中フラグ（コルーチン重複防止）
    private bool playedThisScare = false; // この覗きでSEを鳴らしたか
    private Coroutine scareCo;

    private void Reset()
    {
        // インスペクタの「Reset」で最低限の自動取得（任意）
        if (!goal)
        {
            var byTag = GameObject.FindGameObjectWithTag("Player");
            goal = byTag ? byTag : GameObject.Find("Player");
        }
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        // 参照の最終確認
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!audioSource) audioSource = GetComponent<AudioSource>();

        // AudioSource が無ければ追加（確実に鳴らす）
        if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;  // 2D（必要に応じて3Dへ）
        audioSource.volume = 1f;
        audioSource.mute = false;

        // Agent 初期化
        if (agent)
        {
            agent.speed = speed;
            agent.stoppingDistance = stopDistance;
            agent.autoBraking = true;
            agent.updateRotation = true;
            agent.isStopped = true; // 初期は停止
        }
    }

    private void Update()
    {
        // 参照未設定なら何もしない
        if (!peekCamera) return;

        bool peeking = peekCamera.IsPeeking;

        // 覗き開始 → 追跡開始（1回だけ）
        if (peeking && !running)
        {
            running = true;
            playedThisScare = false;

            if (resetSEIndexEachPeek) seIndex = 0;

            // 覗いた瞬間に必ず1回だけSE再生（距離に依存しない）
            if (!playedThisScare)
            {
                TryPlayOneSEOnce();
                playedThisScare = true;
            }

            if (agent) agent.isStopped = false;
            scareCo = StartCoroutine(Scare());
        }
        // 覗き終了 → 追跡停止
        else if (!peeking && running)
        {
            if (scareCo != null) StopCoroutine(scareCo);
            if (agent) agent.isStopped = true;
            running = false;
        }
    }

    private IEnumerator Scare()
    {
        while (peekCamera && peekCamera.IsPeeking)
        {
            if (goal && agent)
            {
                // 目的地更新
                if (agent.isOnNavMesh)
                {
                    agent.SetDestination(goal.transform.position);
                }

                // 到着判定（保険で少しマージン）
                bool arrived = false;
                if (agent.isOnNavMesh)
                {
                    float stop = Mathf.Max(agent.stoppingDistance, stopDistance);
                    if (!agent.pathPending && agent.remainingDistance <= stop + 0.05f)
                    {
                        arrived = true;
                    }
                }

                // 到着時にも鳴らしたいケースに対応（この覗き中は二重再生しない）
                if (arrived && !playedThisScare)
                {
                    agent.isStopped = true;
                    TryPlayOneSEOnce();
                    playedThisScare = true;
                    break;
                }
            }
            yield return null; // 次フレーム
        }

        running = false;
    }

    /// <summary>
    /// SEを1回だけ再生する。
    /// ・seList を seIndex から一周探索して最初の有効クリップを鳴らす
    /// ・鳴らせたら seIndex を「次」に進める（末尾なら0へ）＝巡回
    /// ・全部 null/空なら fallbackSE でフォールバック
    /// </summary>
    private void TryPlayOneSEOnce()
    {
        if (!audioSource)
        {
            Debug.LogWarning("[PeekScare] AudioSource がありません。");
            return;
        }

        bool played = false;

        if (seList != null && seList.Count > 0)
        {
            int count = seList.Count;
            int start = Mathf.Clamp(seIndex, 0, count - 1);

            for (int n = 0; n < count; n++)
            {
                int idx = (start + n) % count;
                var clip = seList[idx];
                if (clip == null) continue;

                audioSource.PlayOneShot(clip);
                played = true;

                seIndex = (idx + 1) % count; // 巡回
                break;
            }

            if (!played && fallbackSE)
            {
                audioSource.PlayOneShot(fallbackSE);
                played = true;
            }
        }
        else
        {
            if (fallbackSE)
            {
                audioSource.PlayOneShot(fallbackSE);
                played = true;
            }
        }

        if (!played)
        {
            Debug.LogWarning($"[PeekScare] 再生できるSEが見つかりません。listCount:{(seList != null ? seList.Count : 0)} fallbackNull:{fallbackSE == null}");
        }
    }
}
