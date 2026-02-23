using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/*
     このスクリプトがやること

     目的：
     「覗き(PeekCamera.IsPeeking==true)」している間だけ、ゴーストがプレイヤーを追いかける。
     さらに、覗いた瞬間にSEを“必ず1回だけ”鳴らす。

     重要ポイント：
     ・PeekCamera は自動で探さない
       → Inspector で対応する PeekCamera を必ず割り当てる必要がある

     処理の流れ：

     1) Update で peekCamera.IsPeeking を監視する

     2) 覗き開始（IsPeeking=trueになった瞬間）
        ・追跡中フラグ(running)をON
        ・この覗きのSE再生フラグ(playedThisScare)をリセット
        ・必要ならSEのインデックスを0に戻す(resetSEIndexEachPeek)
        ・覗いた瞬間にSEを1回だけ鳴らす（TryPlayOneSEOnce）
        ・NavMeshAgent を動かして追跡コルーチン(Scare)を開始する

     3) 覗き中（Scareコルーチン）
        ・毎フレーム goal（通常はプレイヤー）の位置へ SetDestination
        ・NavMesh上で到着判定を行う（remainingDistance <= stop距離）
        ・到着時に鳴らしたいケースにも対応しているが、
          playedThisScare が true なら二重に鳴らさない

     4) 覗き終了（IsPeeking=falseになった瞬間）
        ・コルーチン停止
        ・NavMeshAgent停止
        ・追跡中フラグ(running)をOFF

     SEの選び方（TryPlayOneSEOnce）
     ・seList がある場合：seIndex から巡回して「最初に見つかったnullじゃないclip」を鳴らす
       鳴らしたら seIndex を次へ進める（末尾なら0へ）
     ・seList が空 / 全部nullの場合：fallbackSE があればそれを鳴らす
     ・それでも鳴らせない場合：Warningログを出す
*/

public class PeekScare : MonoBehaviour
{
    //================
    // References
    //================
    [Header("参照")]
    [SerializeField] private PeekCamera peekCamera;  // 対応する PeekCamera を割り当てる（自動検索しない）
    [SerializeField] private GameObject goal;        // 追跡対象（通常はプレイヤー）
    [SerializeField] private NavMeshAgent agent;     // ゴーストに付けた NavMeshAgent
    [SerializeField] private AudioSource audioSource; // SE再生に使う AudioSource

    //================
    // Move Settings
    //================
    [Header("移動設定")]
    [SerializeField] private float speed = 5f;       // 追跡速度
    [SerializeField] private float stopDistance = 0.3f; // 停止距離（到着判定にも使う）

    //================
    // SE Settings
    //================
    [Header("SE 再生設定")]
    [Tooltip("リストが空 or 使えない時に鳴らす単発SE（フォールバック）")]
    [SerializeField] private AudioClip fallbackSE;

    [Tooltip("複数候補。null要素はスキップ。巡回再生します。")]
    [SerializeField] private List<AudioClip> seList = new List<AudioClip>();

    [Tooltip("覗くたびにインデックスを0に戻したい特殊ケースだけON。通常はOFF推奨。")]
    [SerializeField] private bool resetSEIndexEachPeek = false;

    //================
    // Internal State
    //================
    private int seIndex = 0;              // 次に鳴らす seList の位置（巡回用）
    private bool running = false;         // 追跡中フラグ（コルーチン重複防止）
    private bool playedThisScare = false; // この覗き中にSEを鳴らしたか（二重再生防止）
    private Coroutine scareCo;            // 追跡コルーチン参照

    //================
    // Reset
    //================
    /*
         Inspector の「Reset」を押した時に最低限の自動取得をする（任意）
         ・goal が未設定なら Player タグ or 名前で探す
         ・agent / audioSource が未設定なら自分から取る
    */
    private void Reset()
    {
        if (!goal)
        {
            var byTag = GameObject.FindGameObjectWithTag("Player");
            goal = byTag ? byTag : GameObject.Find("Player");
        }
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    //================
    // Start
    //================
    /*
         必要な参照を保険で揃える
         ・AudioSource がなければ追加して確実に鳴らせるようにする
         ・NavMeshAgent の初期パラメータを設定し、初期は停止状態にする
    */
    private void Start()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!audioSource) audioSource = GetComponent<AudioSource>();

        // AudioSource が無ければ追加（確実に鳴らす）
        if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f;
        audioSource.mute = false;

        // Agent 初期化
        if (agent)
        {
            agent.speed = speed;
            agent.stoppingDistance = stopDistance;
            agent.autoBraking = true;
            agent.updateRotation = true;
            agent.isStopped = true;
        }
    }

    //================
    // Update
    //================
    /*
         peekCamera.IsPeeking を監視して
         ・覗き開始 → 追跡開始 + SE1回再生
         ・覗き終了 → 追跡停止
         を切り替える
    */
    private void Update()
    {
        // PeekCamera 未設定なら何もしない
        if (!peekCamera) return;

        bool peeking = peekCamera.IsPeeking;

        //================
        // 覗き開始：追跡開始
        //================
        if (peeking && !running)
        {
            running = true;
            playedThisScare = false;

            // 必要なら毎回先頭から鳴らす
            if (resetSEIndexEachPeek) seIndex = 0;

            // 覗いた瞬間に必ず1回だけSE再生
            if (!playedThisScare)
            {
                TryPlayOneSEOnce();
                playedThisScare = true;
            }

            if (agent) agent.isStopped = false;
            scareCo = StartCoroutine(Scare());
        }
        //================
        // 覗き終了：追跡停止
        //================
        else if (!peeking && running)
        {
            if (scareCo != null) StopCoroutine(scareCo);
            if (agent) agent.isStopped = true;
            running = false;
        }
    }

    //================
    // Scare Coroutine
    //================
    /*
         覗き中だけループし続ける追跡処理

         ・goal を追いかけるため SetDestination を更新
         ・到着判定を行う（残距離がstop距離以下）
         ・到着時に鳴らしたいケースにも対応している
           ただし playedThisScare で二重再生は防ぐ
    */
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

                // 到着判定（保険で少しマージンを足す）
                bool arrived = false;
                if (agent.isOnNavMesh)
                {
                    float stop = Mathf.Max(agent.stoppingDistance, stopDistance);
                    if (!agent.pathPending && agent.remainingDistance <= stop + 0.05f)
                    {
                        arrived = true;
                    }
                }

                // 到着時にも鳴らしたい場合（ただしこの覗き中は二重再生しない）
                if (arrived && !playedThisScare)
                {
                    agent.isStopped = true;
                    TryPlayOneSEOnce();
                    playedThisScare = true;
                    break;
                }
            }

            yield return null;
        }

        running = false;
    }

    //================
    // SE Play
    //================
    /*
         SEを1回だけ再生する

         再生優先順位：
         1) seList（seIndex から巡回して最初に見つかった有効clip）
            ・null要素は飛ばす
            ・鳴らせたら seIndex を次へ進める（巡回）
         2) fallbackSE（seListが空/全nullの時の保険）

         どれも鳴らせない場合：
         ・警告ログを出す
    */
    private void TryPlayOneSEOnce()
    {
        if (!audioSource)
        {
            Debug.LogWarning("[PeekScare] AudioSource がありません。");
            return;
        }

        bool played = false;

        //================
        // seList から鳴らす
        //================
        if (seList != null && seList.Count > 0)
        {
            int count = seList.Count;
            int start = Mathf.Clamp(seIndex, 0, count - 1);

            // seIndexから順に一周探す
            for (int n = 0; n < count; n++)
            {
                int idx = (start + n) % count;
                var clip = seList[idx];
                if (clip == null) continue;

                audioSource.PlayOneShot(clip);
                played = true;

                // 次回は次のインデックスから（巡回）
                seIndex = (idx + 1) % count;
                break;
            }

            // 全部nullだった場合はfallbackへ
            if (!played && fallbackSE)
            {
                audioSource.PlayOneShot(fallbackSE);
                played = true;
            }
        }
        //================
        // seListが無い場合はfallback
        //================
        else
        {
            if (fallbackSE)
            {
                audioSource.PlayOneShot(fallbackSE);
                played = true;
            }
        }

        //================
        // それでも鳴らせなかった場合
        //================
        if (!played)
        {
            Debug.LogWarning($"[PeekScare] 再生できるSEが見つかりません。listCount:{(seList != null ? seList.Count : 0)} fallbackNull:{fallbackSE == null}");
        }
    }
}