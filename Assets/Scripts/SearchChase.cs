using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class SearchChase : MonoBehaviour
{
    [Header("NavMesh/参照")]
    public NavMeshAgent agent;
    public NavMeshSurface surface;
    public Transform Player;
    public Transform target;
    public Transform lostposition;

    [Header("経路/挙動")]
    public float maxdistance = 2.0f;
    public float repathInterval = 0.25f;
    private float repathtimer = 0f;
    public float StopDistance = 0.5f;
    public float WaitCount = 2.0f;

    [Header("検知/パトロール")]
    public bool isDiscovery = false;
    public List<Transform> targetlist = new List<Transform>();
    int CurrenTtargetNum = 0;
    private bool _foundPrev = false;

    [Header("状態(1=通常探索, 2=追跡モード)")]
    [SerializeField] private int fixedState = 2; // デフォルト2（隠れていても発見可）
    private bool _stateOverridden = false;
    public int GetState() => fixedState;
    public void ForceState(int state)
    {
        int prev = fixedState;
        fixedState = Mathf.Clamp(state, 1, 2);
        _stateOverridden = true;
        if (prev != fixedState)
            Debug.Log($"[SearchChase] 状態変更: {prev} → {fixedState}");
        UpdateStateLabel();
    }

    [Header("隠れ状態参照")]
    public HideCroset HideRef;

    [Header("デバッグ表示（任意）")]
    public TextMeshProUGUI StateLabelTMP;
    public Text StateLabelLegacy;
    [TextArea] public string State1Text = "STATE:1 隠れてる間は安全";
    [TextArea] public string State2Text = "STATE:2 クローゼット内でも発見可";

    [Header("見渡し設定")]
    public float LookInterval = 5f;
    public float LookDuration = 2f;
    public float LookAngle = 360f;
    private float lookTimer = 0f;
    private bool isLooking = false;

    [Header("アニメーション")]
    public Animator animator;
    public string ChaseBoolName = "Chase";

    [Header("怒りエフェクト")]
    public GameObject angryEffectPrefab;
    public Transform angryEffectFollowPoint;
    private GameObject _angryEffectInstance;
    public Vector3 angryEffectLocalOffset = Vector3.zero;

    [Header("捕獲ガード")]
    public bool GuardCatchByState = true;

    [Header("NavMesh更新")]
    public bool enableRuntimeNavmeshUpdate = false;

    [Header("視線(LoS)設定")]
    [Tooltip("幽霊の目の高さ（ワールド座標オフセット）")]
    public float eyeHeight = 1.6f;

    [Tooltip("プレイヤー上半身付近の高さ（ワールド座標オフセット）")]
    public float playerHeight = 1.2f;

    [Tooltip("視線を遮るレイヤー（Enemyなど自分は除外推奨）")]
    public LayerMask losBlockMask = ~0;

    [Tooltip("この距離より遠いプレイヤーは見えない扱い（0以下なら無制限）")]
    public float maxSightRange = 12f;

    void Start()
    {
        if (surface)
        {
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.collectObjects = CollectObjects.All;
            surface.AddData();
        }

        if (!_stateOverridden)
            fixedState = Mathf.Clamp(fixedState, 1, 2);

        if (!HideRef)
            HideRef = FindFirstObjectByType<HideCroset>();

        if (agent)
        {
            agent.stoppingDistance = 0f;
            agent.autoBraking = false;
        }

        UpdateStateLabel();
    }

    void Update()
    {
        // 1) 発見ロジック
        UpdateDiscovery();

        // 2) 発見/未発見の切り替わりトリガ
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
            if (!isLooking && lookTimer >= LookInterval)
            {
                if (agent && agent.isOnNavMesh)
                    StartCoroutine(LookAround());
                else
                    lookTimer = 0f;
            }
        }
        else
        {
            lookTimer = 0f;
        }

        // 4) 経路更新
        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0f;

            if (surface && enableRuntimeNavmeshUpdate)
                surface.UpdateNavMesh(surface.navMeshData);

            // NavMesh上にいるときだけ経路関連の処理を行う
            if (agent && agent.isOnNavMesh)
            {
                // プレイヤー未発見時のパトロール／最後に見た位置まで追う処理
                if (!isDiscovery && !isLooking)
                {
                    // 「最後に見た位置(lostposition)」に向かっている場合
                    if (target == lostposition && lostposition)
                    {
                        // そこにほぼ到達したらパトロール復帰
                        if (!agent.pathPending && agent.remainingDistance <= StopDistance)
                        {
                            agent.isStopped = true;
                            TargetChange(); // 到達可能なパトロールポイントへ
                        }
                    }
                    // パトロール中（targetlistのいずれかを追っている）場合
                    else if (targetlist.Count > 0)
                    {
                        // 現在のターゲットが行けない場所になっていたら次を探す
                        if (!target || !IsReachable(target.position))
                        {
                            agent.isStopped = true;
                            TargetChange();
                        }
                        else
                        {
                            // 現在ターゲットに到達したら次のパトロールポイントへ
                            if (!agent.pathPending && agent.remainingDistance <= StopDistance)
                            {
                                agent.isStopped = true;
                                TargetChange();
                            }
                        }
                    }
                }

                // 実際の移動（追跡／パトロール共通）
                if (!isLooking)
                    ChaseMove();
            }
        }

        // 5) NavMesh 安全化
        EnsureAgentOnNavMesh();
    }

    // ========== NavMesh に従って目的地へ動く ==========
    void ChaseMove()
    {
        if (!agent || !agent.isOnNavMesh || !target) return;

        if (NavMesh.SamplePosition(target.position, out var hit, maxdistance, NavMesh.AllAreas))
        {
            agent.ResetPath();
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    // ========== NavMesh 安全化 ==========
    void EnsureAgentOnNavMesh()
    {
        if (!agent) return;
        if (!agent.isOnNavMesh &&
            NavMesh.SamplePosition(agent.transform.position, out var hit, 0.5f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }
    }

    // ========== 次のパトロール地点に回す ==========
    void TargetChange()
    {
        if (!agent) return;
        if (!agent.isStopped) return;
        if (targetlist.Count == 0) return;

        int tries = 0;
        int idx = CurrenTtargetNum;

        // 現状到達可能なポイントのみを採用してループ
        while (tries < targetlist.Count)
        {
            idx = (idx + 1) % targetlist.Count;
            Transform cand = targetlist[idx];

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

        // どこにも行けない場合はその場に留まる
        Debug.Log("[SearchChase] 到達可能なパトロールポイントがありません");
    }

    // ========== 現在位置から目的地まで到達可能かチェック ==========
    bool IsReachable(Vector3 dest)
    {
        if (!agent || !agent.isOnNavMesh) return false;

        var path = new NavMeshPath();
        if (!agent.CalculatePath(dest, path))
            return false;

        return path.status == NavMeshPathStatus.PathComplete;
    }

    // ========== 発見ロジック（LoS＋隠れ例外） ==========
    private void UpdateDiscovery()
    {
        if (!Player)
        {
            isDiscovery = false;
            return;
        }

        bool hidden = HideRef && HideRef.hide;

        // 先に state と隠れの例外を処理
        if (fixedState == 1)
        {
            if (hidden)
            {
                isDiscovery = false; // state1 かつ 隠れ中は絶対未発見
                return;
            }
        }
        else if (fixedState == 2)
        {
            if (hidden)
            {
                isDiscovery = true;  // state2 かつ 隠れ中は無条件発見
                UpdateLostAndTarget();
                return;
            }
        }

        // それ以外は LoS 判定（上限距離あり）
        isDiscovery = HasLineOfSightToPlayer();
        if (isDiscovery) UpdateLostAndTarget();
    }

    // いまのプレイヤー位置を lostposition に記憶＆追跡ターゲットに
    private void UpdateLostAndTarget()
    {
        if (lostposition)
        {
            lostposition.position = Player.position;
            target = lostposition;
        }
        else
        {
            target = Player; // 保険
        }
    }

    // ========== LoSチェック：Raycastで間に何があるかを見る（距離上限付き） ==========
    private bool HasLineOfSightToPlayer()
    {
        if (!Player) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = Player.position + Vector3.up * playerHeight;

        Vector3 dir = targetPos - origin;
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return true;

        // 距離上限が有効なら、上限を超えている時点で見えない扱い
        if (maxSightRange > 0f && dist > maxSightRange)
        {
            // Debug.Log($"[LoS] 距離超過 dist={dist:F2} > max={maxSightRange:F2}");
            return false;
        }

        dir /= dist;
        float rayLen = (maxSightRange > 0f) ? Mathf.Min(dist, maxSightRange) : dist;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, rayLen, losBlockMask, QueryTriggerInteraction.Ignore))
        {
            // 最初に当たったのがプレイヤー（子含む）なら見えている
            if (hit.collider.CompareTag("Player") || hit.collider.transform.root.CompareTag("Player"))
                return true;

            // それ以外に当たった＝遮蔽物に遮られた
            return false;
        }

        // 何にも当たらない＝遮蔽物なし＝見えている
        return true;
    }

    // 画面デバッグ用ラベル更新
    private void UpdateStateLabel()
    {
        string msg = (fixedState == 1) ? State1Text : State2Text;
        if (StateLabelTMP) StateLabelTMP.text = msg;
        if (StateLabelLegacy) StateLabelLegacy.text = msg;
    }

    // ====== 見渡しモーション（ぐるっと回る） ======
    IEnumerator LookAround()
    {
        isLooking = true;
        float elapsed = 0f;
        float turnSpeed = (LookDuration > 0f) ? (LookAngle / LookDuration) : 0f;

        CancelInvoke(nameof(TargetChange));

        if (agent)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.updateRotation = false;
        }

        // 回している間も発見チェックを継続。発見したら即中断して追跡へ。
        while (elapsed < LookDuration)
        {
            UpdateDiscovery();
            if (isDiscovery) break;

            transform.Rotate(0f, turnSpeed * Time.deltaTime, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        isLooking = false;
        lookTimer = 0f;

        if (agent)
        {
            agent.updateRotation = true;
            agent.isStopped = false;
        }

        // 直近の target（発見中なら lostposition）に向けて再セット
        ChaseMove();
    }

    // ====== AnimatorのChaseフラグ制御 ======
    private void SetChaseAnim(bool chasing)
    {
        if (!animator || string.IsNullOrEmpty(ChaseBoolName)) return;
        animator.SetBool(ChaseBoolName, chasing);
    }

    // ====== 怒りエフェクトを出す（追跡開始時） ======
    private void SpawnAngryEffect()
    {
        if (!angryEffectPrefab || _angryEffectInstance) return;
        Transform follow = angryEffectFollowPoint ? angryEffectFollowPoint : this.transform;
        _angryEffectInstance = Instantiate(angryEffectPrefab, follow);
        _angryEffectInstance.transform.localPosition = angryEffectLocalOffset;
    }

    // ====== 怒りエフェクトを消す（見失ったとき） ======
    private void DespawnAngryEffect()
    {
        if (_angryEffectInstance)
        {
            Destroy(_angryEffectInstance);
            _angryEffectInstance = null;
        }
    }

    // ====== 捕獲イベントのガード（このスクリプトにCollider/Triggerが来る場合のみ） ======
    private void OnTriggerEnter(Collider other)
    {
        if (!GuardCatchByState) return;
        if (fixedState == 1 && HideRef && HideRef.hide) return;
        // ここで捕獲処理をしている場合は引き続き…
    }

    private void OnTriggerStay(Collider other)
    {
        if (!GuardCatchByState) return;
        if (fixedState == 1 && HideRef && HideRef.hide) return;
        // ここで捕獲処理をしている場合は引き続き…
    }

    // ====== デバッグ可視化 ======
    private void OnDrawGizmosSelected()
    {
        if (!Player) return;

        Gizmos.color = isDiscovery ? Color.red : Color.cyan;
        Vector3 a = transform.position + Vector3.up * eyeHeight;
        Vector3 b = Player.position + Vector3.up * playerHeight;
        Gizmos.DrawLine(a, b);

        // 視認距離の上限の可視化（任意）
        if (maxSightRange > 0f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, maxSightRange);
        }
    }
}
