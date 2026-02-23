using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class SearchChase : MonoBehaviour
{
    /*
        =====================================================================
        SearchChase がやっていること
        =====================================================================

        ■目的
        ・幽霊（NavMeshAgent）を「巡回」させつつ、プレイヤーを「発見したら追跡」する。

        ■大きく 5つ の処理を Update で回している
        1) UpdateDiscovery()
           - プレイヤーを発見したかどうか（isDiscovery）を更新する
           - LoS（視線が通ってるか Raycast）＋ 隠れ状態（HideCroset）＋ state(1/2) で決める

        2) 発見/見失いの瞬間処理
           - isDiscovery が切り替わった瞬間に
             ・Animator の Chase フラグ
             ・怒りエフェクト
             を ON/OFF する

        3) 見渡し（LookAround）
           - 未発見のときだけ、一定間隔でその場回転して探す演出
           - 見渡し中も UpdateDiscovery() を回し、見つけたら即中断して追跡へ

        4) 経路更新（一定間隔 repathInterval）
           - パトロール中：到達したら次の巡回地点へ
           - 「最後に見た位置（lostposition）」を追って到達したら巡回復帰
           - 追跡/巡回に関わらず、ChaseMove() で agent.SetDestination する

        5) NavMesh 安全化
           - agent が NavMesh 上から外れてたら Warp で戻す（落下や押し出し対策）

        ■状態 fixedState の意味（重要）
        - fixedState == 1：
            プレイヤーが HideCroset.hide == true（隠れ中）なら「絶対に未発見」
        - fixedState == 2：
            プレイヤーが隠れ中でも「無条件で発見扱い」
            （クローゼットに逃げても安全じゃないモード）

        ■isDiscovery が true になった時に起きること
        - lostposition を Player の場所に更新し、
          target = lostposition にして追跡する（＝最後に見た位置を追う）

        =====================================================================
    */

    // =========================
    // NavMesh / 参照
    // =========================
    [Header("NavMesh/参照")]
    public NavMeshAgent agent;     // 幽霊の移動担当（NavMeshAgent）
    public NavMeshSurface surface; // runtime更新したい場合の NavMeshSurface
    public Transform Player;       // プレイヤー参照
    public Transform target;       // いま向かう目的地（巡回先 or lostposition）
    public Transform lostposition; // 「最後に見た位置」を置くための Transform

    // =========================
    // 経路 / 挙動
    // =========================
    [Header("経路/挙動")]
    public float maxdistance = 2.0f;      // SamplePosition の検索半径
    public float repathInterval = 0.25f;  // SetDestination 更新の間隔
    private float repathtimer = 0f;

    public float StopDistance = 0.5f; // 目的地到達判定の距離
    public float WaitCount = 2.0f;    // ※現状この変数はロジックで使っていない（将来用？）

    // =========================
    // 検知 / パトロール
    // =========================
    [Header("検知/パトロール")]
    public bool isDiscovery = false;                 // プレイヤー発見中？
    public List<Transform> targetlist = new();       // 巡回ポイント一覧
    int CurrenTtargetNum = 0;                        // 現在の巡回インデックス
    private bool _foundPrev = false;                 // 前フレームの発見状態（切り替わり検出用）

    // =========================
    // 状態 (1/2)
    // =========================
    [Header("状態(1=通常探索, 2=追跡モード)")]
    [SerializeField] private int fixedState = 2; // デフォルト2（隠れていても発見可）
    private bool _stateOverridden = false;

    // 外部から状態を知りたいとき用
    public int GetState() => fixedState;

    // 外部から強制で状態を変更したいとき用
    public void ForceState(int state)
    {
        int prev = fixedState;

        // 1〜2 以外は入らないように固定
        fixedState = Mathf.Clamp(state, 1, 2);

        // Start() の初期化で上書きされないようにフラグを立てる
        _stateOverridden = true;

        // 変わったときだけログ
        if (prev != fixedState)
            Debug.Log($"[SearchChase] 状態変更: {prev} → {fixedState}");

        // UIラベル更新
        UpdateStateLabel();
    }

    // =========================
    // 隠れ状態参照
    // =========================
    [Header("隠れ状態参照")]
    public HideCroset HideRef; // プレイヤーが隠れ中かを見る参照（HideRef.hide）

    // =========================
    // デバッグ表示（任意）
    // =========================
    [Header("デバッグ表示（任意）")]
    public TextMeshProUGUI StateLabelTMP;
    public Text StateLabelLegacy;

    [TextArea] public string State1Text = "STATE:1 隠れてる間は安全";
    [TextArea] public string State2Text = "STATE:2 クローゼット内でも発見可";

    // =========================
    // 見渡し設定
    // =========================
    [Header("見渡し設定")]
    public float LookInterval = 5f;    // 何秒ごとに見渡しをするか
    public float LookDuration = 2f;    // 見渡し時間
    public float LookAngle = 360f;     // 何度回るか
    private float lookTimer = 0f;
    private bool isLooking = false;

    // =========================
    // アニメーション
    // =========================
    [Header("アニメーション")]
    public Animator animator;
    public string ChaseBoolName = "Chase"; // Animator の bool パラメータ名

    // =========================
    // 怒りエフェクト
    // =========================
    [Header("怒りエフェクト")]
    public GameObject angryEffectPrefab;          // 追跡開始時のエフェクトPrefab
    public Transform angryEffectFollowPoint;      // エフェクトを付ける場所（頭など）
    private GameObject _angryEffectInstance;      // 生成済みインスタンス
    public Vector3 angryEffectLocalOffset = Vector3.zero;

    // =========================
    // 捕獲ガード（このスクリプト側トリガーに捕獲処理がある場合の保険）
    // =========================
    [Header("捕獲ガード")]
    public bool GuardCatchByState = true;

    // =========================
    // NavMesh更新
    // =========================
    [Header("NavMesh更新")]
    public bool enableRuntimeNavmeshUpdate = false;

    // =========================
    // 視線(LoS)設定
    // =========================
    [Header("視線(LoS)設定")]
    [Tooltip("幽霊の目の高さ（ワールド座標オフセット）")]
    public float eyeHeight = 1.6f;

    [Tooltip("プレイヤー上半身付近の高さ（ワールド座標オフセット）")]
    public float playerHeight = 1.2f;

    [Tooltip("視線を遮るレイヤー（Enemyなど自分は除外推奨）")]
    public LayerMask losBlockMask = ~0;

    [Tooltip("この距離より遠いプレイヤーは見えない扱い（0以下なら無制限）")]
    public float maxSightRange = 12f;

    // =========================
    // Start
    // =========================
    void Start()
    {
        // NavMeshSurface を持っている場合：初期化設定
        if (surface)
        {
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.collectObjects = CollectObjects.All;
            surface.AddData();
        }

        // 外部から ForceState されていない時だけ、1〜2に丸める
        if (!_stateOverridden)
            fixedState = Mathf.Clamp(fixedState, 1, 2);

        // 隠れ参照が未設定なら探す
        if (!HideRef)
            HideRef = FindFirstObjectByType<HideCroset>();

        // agent 初期設定（必要なら調整）
        if (agent)
        {
            agent.stoppingDistance = 0f;
            agent.autoBraking = false;
        }

        // 状態表示UI更新
        UpdateStateLabel();
    }

    // =========================
    // Update（メインループ）
    // =========================
    void Update()
    {
        // 1) 発見ロジックを更新（isDiscovery がここで決まる）
        UpdateDiscovery();

        // 2) 発見/未発見が切り替わった瞬間の処理
        if (isDiscovery != _foundPrev)
        {
            if (isDiscovery)
            {
                Debug.Log("[SearchChase] プレイヤー発見");
                SetChaseAnim(true);
                SpawnAngryEffect();
            }
            else
            {
                Debug.Log("[SearchChase] 見失い");
                SetChaseAnim(false);
                DespawnAngryEffect();
            }
        }
        _foundPrev = isDiscovery;

        // 3) 見渡し（未発見のときだけ）
        if (!isDiscovery)
        {
            lookTimer += Time.deltaTime;

            // 一定時間たったら見渡し開始
            if (!isLooking && lookTimer >= LookInterval)
            {
                // NavMesh上なら回れる（NavMesh外なら回す意味が薄いのでスキップ）
                if (agent && agent.isOnNavMesh)
                    StartCoroutine(LookAround());
                else
                    lookTimer = 0f;
            }
        }
        else
        {
            // 発見中は見渡しタイマーをリセット
            lookTimer = 0f;
        }

        // 4) 経路更新（一定間隔）
        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0f;

            // runtime navmesh 更新（必要な時だけ）
            if (surface && enableRuntimeNavmeshUpdate)
                surface.UpdateNavMesh(surface.navMeshData);

            // agent が NavMesh上にいる時だけ経路処理をする
            if (agent && agent.isOnNavMesh)
            {
                // ---- 未発見 ＆ 見渡し中じゃないとき：巡回ロジック ----
                if (!isDiscovery && !isLooking)
                {
                    // ①「最後に見た位置(lostposition)」を追っている最中
                    if (target == lostposition && lostposition)
                    {
                        // 到達したら巡回へ戻す
                        if (!agent.pathPending && agent.remainingDistance <= StopDistance)
                        {
                            agent.isStopped = true;
                            TargetChange();
                        }
                    }
                    // ② 巡回中
                    else if (targetlist.Count > 0)
                    {
                        // 今の target が壊れた/行けなくなった → 次へ
                        if (!target || !IsReachable(target.position))
                        {
                            agent.isStopped = true;
                            TargetChange();
                        }
                        else
                        {
                            // 到達した → 次へ
                            if (!agent.pathPending && agent.remainingDistance <= StopDistance)
                            {
                                agent.isStopped = true;
                                TargetChange();
                            }
                        }
                    }
                }

                // ---- 移動処理（追跡/巡回 共通）----
                if (!isLooking)
                    ChaseMove();
            }
        }

        // 5) NavMesh 安全化（NavMesh外に出たら戻す）
        EnsureAgentOnNavMesh();
    }

    // =========================================================
    // NavMesh に従って目的地へ動く（agent.SetDestination）
    // =========================================================
    void ChaseMove()
    {
        // agent / target がない or NavMesh外なら何もしない
        if (!agent || !agent.isOnNavMesh || !target) return;

        // target.position が NavMesh上のどこに近いかを取得してそこへ向かう
        if (NavMesh.SamplePosition(target.position, out var hit, maxdistance, NavMesh.AllAreas))
        {
            agent.ResetPath();
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    // =========================================================
    // NavMesh 安全化（agent が NavMesh外なら Warp で戻す）
    // =========================================================
    void EnsureAgentOnNavMesh()
    {
        if (!agent) return;

        if (!agent.isOnNavMesh &&
            NavMesh.SamplePosition(agent.transform.position, out var hit, 0.5f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }
    }

    // =========================================================
    // 次のパトロール地点へ変更
    // ・到達可能なポイントだけを採用する
    // =========================================================
    void TargetChange()
    {
        if (!agent) return;
        if (!agent.isStopped) return;          // 止まってないなら変更しない
        if (targetlist.Count == 0) return;

        int tries = 0;
        int idx = CurrenTtargetNum;

        // 全ポイントを最大1周ぶん試す
        while (tries < targetlist.Count)
        {
            idx = (idx + 1) % targetlist.Count;
            Transform cand = targetlist[idx];

            // cand が存在し、かつ到達可能なら採用
            if (cand && IsReachable(cand.position))
            {
                CurrenTtargetNum = idx;
                target = cand;
                agent.isStopped = false;
                ChaseMove();
                return;
            }

            tries++;
        }

        // どれも到達できない → その場に留まる
        Debug.Log("[SearchChase] 到達可能なパトロールポイントがありません");
    }

    // =========================================================
    // 現在位置から dest まで NavMeshPath が完了するか？
    // =========================================================
    bool IsReachable(Vector3 dest)
    {
        if (!agent || !agent.isOnNavMesh) return false;

        var path = new NavMeshPath();
        if (!agent.CalculatePath(dest, path))
            return false;

        return path.status == NavMeshPathStatus.PathComplete;
    }

    // =========================================================
    // 発見ロジック（state + 隠れ + LoS）
    // =========================================================
    private void UpdateDiscovery()
    {
        // プレイヤー参照がないなら未発見
        if (!Player)
        {
            isDiscovery = false;
            return;
        }

        // 隠れ中？
        bool hidden = HideRef && HideRef.hide;

        // ---- まず state と 隠れ の例外を最優先で処理 ----
        if (fixedState == 1)
        {
            // state1：隠れている間は絶対に未発見
            if (hidden)
            {
                isDiscovery = false;
                return;
            }
        }
        else if (fixedState == 2)
        {
            // state2：隠れていても無条件で発見扱い
            if (hidden)
            {
                isDiscovery = true;
                UpdateLostAndTarget();
                return;
            }
        }

        // ---- 上の例外に当たらない場合だけ LoS 判定 ----
        isDiscovery = HasLineOfSightToPlayer();

        // 発見できたら「最後に見た位置」を更新し target を追跡用にする
        if (isDiscovery)
            UpdateLostAndTarget();
    }

    // =========================================================
    // プレイヤー位置を lostposition に記憶し target を更新
    // =========================================================
    private void UpdateLostAndTarget()
    {
        // lostposition があるなら「そこを追う」
        if (lostposition)
        {
            lostposition.position = Player.position;
            target = lostposition;
        }
        else
        {
            // 無ければ直接 Player を追う（保険）
            target = Player;
        }
    }

    // =========================================================
    // LoSチェック（Raycast）
    // ・幽霊の目の位置 → プレイヤー上半身へ Ray を飛ばす
    // ・途中で何かに当たったら遮蔽物扱い
    // =========================================================
    private bool HasLineOfSightToPlayer()
    {
        if (!Player) return false;

        // Ray の開始点（幽霊の目）
        Vector3 origin = transform.position + Vector3.up * eyeHeight;

        // Ray の目標点（プレイヤー上半身）
        Vector3 targetPos = Player.position + Vector3.up * playerHeight;

        Vector3 dir = targetPos - origin;
        float dist = dir.magnitude;

        // 距離がほぼ0なら見えてる扱い
        if (dist <= 0.0001f) return true;

        // 距離上限チェック（有効な場合）
        if (maxSightRange > 0f && dist > maxSightRange)
            return false;

        dir /= dist;

        // 実際に飛ばす長さ（上限がある場合は短くする）
        float rayLen = (maxSightRange > 0f) ? Mathf.Min(dist, maxSightRange) : dist;

        // Raycast して最初に当たったものを見る
        if (Physics.Raycast(origin, dir, out RaycastHit hit, rayLen, losBlockMask, QueryTriggerInteraction.Ignore))
        {
            // 最初に当たったのがプレイヤーなら視線が通っている
            if (hit.collider.CompareTag("Player") || hit.collider.transform.root.CompareTag("Player"))
                return true;

            // それ以外に当たったなら遮蔽物で見えない
            return false;
        }

        // 何にも当たらない＝遮蔽物なし＝見えている
        return true;
    }

    // =========================================================
    // 状態表示UI更新（TMP/Legacy両対応）
    // =========================================================
    private void UpdateStateLabel()
    {
        string msg = (fixedState == 1) ? State1Text : State2Text;
        if (StateLabelTMP) StateLabelTMP.text = msg;
        if (StateLabelLegacy) StateLabelLegacy.text = msg;
    }

    // =========================================================
    // 見渡しモーション
    // ・その場で回転しながら探索する
    // ・回転中も発見チェックし、見つけたら中断して追跡へ
    // =========================================================
    IEnumerator LookAround()
    {
        isLooking = true;

        float elapsed = 0f;
        float turnSpeed = (LookDuration > 0f) ? (LookAngle / LookDuration) : 0f;

        // 予約してる TargetChange があるなら止める（保険）
        CancelInvoke(nameof(TargetChange));

        // 見渡し中は agent を止める
        if (agent)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.updateRotation = false;
        }

        // 指定秒数ぶん回る
        while (elapsed < LookDuration)
        {
            // 回っている最中も発見チェック
            UpdateDiscovery();
            if (isDiscovery) break;

            // 自分自身を Y 軸回転
            transform.Rotate(0f, turnSpeed * Time.deltaTime, 0f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 終了処理
        isLooking = false;
        lookTimer = 0f;

        if (agent)
        {
            agent.updateRotation = true;
            agent.isStopped = false;
        }

        // 最終的な target に向けて再設定（見つけてたら lostposition）
        ChaseMove();
    }

    // =========================================================
    // Animator の Chase bool を切り替える
    // =========================================================
    private void SetChaseAnim(bool chasing)
    {
        if (!animator || string.IsNullOrEmpty(ChaseBoolName)) return;
        animator.SetBool(ChaseBoolName, chasing);
    }

    // =========================================================
    // 怒りエフェクト生成（追跡開始時）
    // =========================================================
    private void SpawnAngryEffect()
    {
        if (!angryEffectPrefab || _angryEffectInstance) return;

        // 追従ポイントがあればそこ、なければ自分に付ける
        Transform follow = angryEffectFollowPoint ? angryEffectFollowPoint : this.transform;

        _angryEffectInstance = Instantiate(angryEffectPrefab, follow);
        _angryEffectInstance.transform.localPosition = angryEffectLocalOffset;
    }

    // =========================================================
    // 怒りエフェクト消去（見失い時）
    // =========================================================
    private void DespawnAngryEffect()
    {
        if (_angryEffectInstance)
        {
            Destroy(_angryEffectInstance);
            _angryEffectInstance = null;
        }
    }

    // =========================================================
    // 捕獲イベントのガード（ここに捕獲処理がある場合の保険）
    // ※このスクリプト内では捕獲処理は書いていない（コメントだけ）
    // =========================================================
    private void OnTriggerEnter(Collider other)
    {
        if (!GuardCatchByState) return;

        // state1 で隠れてる間は捕獲しない
        if (fixedState == 1 && HideRef && HideRef.hide) return;

        // ここに捕獲処理を書くならこの下に（例：ゲームオーバーなど）
    }

    private void OnTriggerStay(Collider other)
    {
        if (!GuardCatchByState) return;

        // state1 で隠れてる間は捕獲しない
        if (fixedState == 1 && HideRef && HideRef.hide) return;

        // ここに捕獲処理を書くならこの下に
    }

    // =========================================================
    // デバッグ可視化（Sceneビュー）
    // =========================================================
    private void OnDrawGizmosSelected()
    {
        if (!Player) return;

        // 視線ライン（発見中は赤、未発見は水色）
        Gizmos.color = isDiscovery ? Color.red : Color.cyan;
        Vector3 a = transform.position + Vector3.up * eyeHeight;
        Vector3 b = Player.position + Vector3.up * playerHeight;
        Gizmos.DrawLine(a, b);

        // 視認距離上限のワイヤー球
        if (maxSightRange > 0f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, maxSightRange);
        }
    }
}