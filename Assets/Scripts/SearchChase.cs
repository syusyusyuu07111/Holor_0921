// 巡回しながら探して、見つけたら追いかけるスクリプト（LoS 必須＋クローゼット例外）
// 仕様：
//  state1 … ★隠れている間（HideCroset.hide==true）は絶対に未発見＆非捕獲。隠れていなければ「視線が通れば」発見。
//  state2 … クローゼット内なら LoS を無視して即発見。それ以外は「視線が通れば」発見。
//  ※「家具など通常の遮蔽物」がプレイヤーと幽霊の間にある場合は、LoS が遮られて未発見。
//  ※「クローゼット（HideCroset.hide==true）」の中は state2 のときだけ例外的に必ず発見。

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;                 // デバッグ用（任意）
using UnityEngine.UI;        // 旧UI.Text用（任意）
using System.Collections;    // コルーチン

public class SearchChase : MonoBehaviour
{
    [Header("NavMesh/参照")]
    public NavMeshAgent agent;
    public NavMeshSurface surface;
    public Transform Player;         // 追う対象（プレイヤー）
    public Transform target;         // 現在の目的地（lostposition か パトロール点）
    public Transform lostposition;   // 最後に見失った位置（Emptyなど）

    [Header("経路/挙動")]
    public float maxdistance = 2.0f;       // NavMesh.SamplePosition の半径
    public float repathInterval = 0.25f;   // 経路再計算間隔
    private float repathtimer = 0f;
    public float StopDistance = 0.5f;      // パトロール到達のしきい
    public float WaitCount = 2.0f;         // 次ウェイポイントに行く前の待ち秒

    [Header("検知/パトロール")]
    public bool isDiscovery = false;       // プレイヤーを見つけてる状態？
    public List<Transform> targetlist = new List<Transform>();
    int CurrenTtargetNum = 0;

    // 直前の isDiscovery を保持して「切り替わり」を検知する
    private bool _foundPrev = false;

    // --------------- 状態(1 or 2) ---------------
    [SerializeField] private int fixedState = 1; // 1 or 2
    private bool _stateOverridden = false;
    public int GetState() => fixedState;
    public void ForceState(int state)
    {
        fixedState = Mathf.Clamp(state, 1, 2);
        _stateOverridden = true;
        UpdateStateLabel();
    }

    // --------------- 隠れ状態参照 ---------------
    public HideCroset HideRef; // HideCroset.hide == true で「クローゼットに隠れてる」

    // --------------- デバッグ表示 ---------------
    [Header("デバッグ表示（任意）")]
    public TextMeshProUGUI StateLabelTMP;
    public Text StateLabelLegacy;
    [TextArea] public string State1Text = "STATE: 1  隠れてる間は絶対バレない/捕まらない（非隠れ時はLoSで発見）";
    [TextArea] public string State2Text = "STATE: 2  クローゼット内でも見つかる（LoSほぼ無視）";

    // ===== 見渡し（サーチ） =====
    [Header("見渡し（サーチ挙動）")]
    public float LookInterval = 5.0f;    // 何秒ごとに首を回して探すか
    public float LookDuration = 2.0f;    // その見渡しをどのくらい続けるか
    public float LookAngle = 360f;       // 何度ぶん回すか
    private float lookTimer = 0f;
    private bool isLooking = false;      // 今「見渡し行動中」か

    // ===== アニメーション制御 =====
    [Header("アニメーション")]
    public Animator animator;
    [Tooltip("追跡中にtrueになるフラグ名(AnimatorのBooleanパラメータ)")]
    public string ChaseBoolName = "Chase";

    // ===== 追跡エフェクト =====
    [Header("怒りエフェクト（追跡時だけ出す）")]
    public GameObject angryEffectPrefab;          // 怒りオーラとか
    public Transform angryEffectFollowPoint;      // エフェクトを付けたい場所(頭の空オブジェとか)。nullならこの幽霊自身
    private GameObject _angryEffectInstance;      // 実際に出したやつ

    // ===== 追跡エフェクト位置オフセット（任意）=====
    [Tooltip("エフェクトを頭の少し上などに浮かせたいならここで調整(ローカル座標)")]
    public Vector3 angryEffectLocalOffset = Vector3.zero;

    // ===== 捕獲ガード（このスクリプトに Trigger が来る場合だけ有効）=====
    [Header("捕獲ガード（任意）")]
    public bool GuardCatchByState = true; // true の間、state1 かつ隠れ中は OnTrigger で絶対捕獲させない

    void Start()
    {
        // NavMesh 準備
        if (surface)
        {
            surface.navMeshData = new NavMeshData(surface.agentTypeID);
            surface.AddData();
            surface.collectObjects = CollectObjects.All;
        }

        // state 未固定ならランダム 1 or 2
        if (!_stateOverridden)
            fixedState = Random.Range(1, 3); // 上限は排他的なので1か2

        UpdateStateLabel();

        // NavMeshAgentの挙動を追跡向けに寄せる
        if (agent)
        {
            agent.stoppingDistance = 0f;  // できるだけ近づく
            agent.autoBraking = false;    // 減速しないで詰める
        }
    }

    void Update()
    {
        // 1. 発見ロジック更新
        UpdateDiscovery();

        // 2. 切り替わり（未発見→発見 / 発見→未発見）を検知
        if (isDiscovery != _foundPrev)
        {
            if (isDiscovery)
            {
                Debug.Log("見つけたので追跡開始");

                // AnimatorのChaseフラグON
                SetChaseAnim(true);

                // 怒りエフェクトを出す
                SpawnAngryEffect();
            }
            else
            {
                Debug.Log("見失ったので探索に戻る");

                // AnimatorのChaseフラグOFF
                SetChaseAnim(false);

                // 怒りエフェクトを消す
                DespawnAngryEffect();
            }
        }
        _foundPrev = isDiscovery;

        // 3. 見渡し行動（未発見のときだけ）
        if (!isDiscovery)
        {
            lookTimer += Time.deltaTime;
            if (!isLooking && lookTimer >= LookInterval)
            {
                if (agent && agent.isOnNavMesh)
                    StartCoroutine(LookAround());
                else
                    lookTimer = 0f; // NavMesh外なら仕切り直し
            }
        }
        else
        {
            lookTimer = 0f;
        }

        // 4. 経路更新（一定間隔で再セット）
        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0f;
            if (surface) surface.UpdateNavMesh(surface.navMeshData);

            if (!isLooking) // 見渡し中は足止め
                ChaseMove();
        }

        // 5. NavMeshの安全確認
        EnsureAgentOnNavMesh();

        // 6. パトロール/待機系
        if (agent && agent.hasPath && !agent.pathPending)
        {
            if (isDiscovery)
            {
                // 追跡中は止まらず突っ込む
                agent.isStopped = false;
            }
            else
            {
                // 未発見のとき waypoint に到達したら足止め→次の目標へ
                if (agent.remainingDistance <= StopDistance)
                {
                    agent.isStopped = true;
                    Invoke(nameof(TargetChange), WaitCount);
                }
            }
        }
    }

    // ========== NavMesh に従って目的地へ動く ==========
    void ChaseMove()
    {
        if (!agent || !agent.isOnNavMesh || !target) return;

        // 見つけてる間は lostposition(=最後に見たプレイヤー位置)、
        // 見失ってる間はパトロール先へ
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
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(agent.transform.position, out var hit, 0.5f, NavMesh.AllAreas))
                agent.Warp(hit.position);
        }
    }

    // ========== 次のパトロール地点に回す ==========
    void TargetChange()
    {
        if (!agent) return;
        if (!agent.isStopped) return; // 既に動き始めてたらスキップ

        CurrenTtargetNum++;
        if (targetlist.Count <= CurrenTtargetNum) CurrenTtargetNum = 0;
        if (targetlist.Count > 0) target = targetlist[CurrenTtargetNum];

        agent.isStopped = false;
        ChaseMove();
    }

    // ========== 発見ロジック（LoS＋クローゼット例外） ==========
    private void UpdateDiscovery()
    {
        if (!Player)
        {
            isDiscovery = false;
            return;
        }

        // state1：
        //   隠れてたら絶対に未発見（早期return）
        //   隠れていなければ LoS が通れば発見
        if (fixedState == 1)
        {
            if (HideRef && HideRef.hide)
            {
                isDiscovery = false;    // ←絶対にバレない
                return;
            }

            isDiscovery = HasLineOfSightToPlayer();
            if (isDiscovery) UpdateLostAndTarget();
            return;
        }

        // state2：
        //   クローゼット中(HideRef.hide==true)なら即発見（LoS無視）
        //   それ以外は LoS が通れば発見
        if (fixedState == 2)
        {
            if (HideRef && HideRef.hide)
            {
                isDiscovery = true;
                UpdateLostAndTarget();
                return;
            }

            isDiscovery = HasLineOfSightToPlayer();
            if (isDiscovery) UpdateLostAndTarget();
            return;
        }

        // フォールバック
        isDiscovery = HasLineOfSightToPlayer();
        if (isDiscovery) UpdateLostAndTarget();
    }

    // いまのプレイヤー位置を lostposition に記憶し、追跡ターゲットにする
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

    // LoSチェック：Raycastで間に何があるか見る
    private bool HasLineOfSightToPlayer()
    {
        Vector3 origin = transform.position;
        Vector3 dir = (Player.position - origin);
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return true;

        dir /= dist; // 正規化

        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            // まっさきにPlayerに当たれば見えてる扱い
            return hit.collider.CompareTag("Player");
        }

        // 何にも当たらない＝遮蔽物なし＝見えてる扱い
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

        // ウェイポイントのInvokeとかぶらないように
        CancelInvoke(nameof(TargetChange));

        if (agent)
        {
            agent.isStopped = true;            // まず止まる
            agent.ResetPath();                 // 経路クリア
            agent.velocity = Vector3.zero;     // 慣性殺す
            agent.updateRotation = false;      // 回頭は手動に
        }

        // 回ってる間も発見判定は継続。見つけたら中断して即追跡モードへ
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
            agent.updateRotation = true;       // 自動回頭戻す
            agent.isStopped = false;           // 移動再開
        }

        // いまの target（発見中なら lostposition）に向けて再セット
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
        if (angryEffectPrefab == null) return;
        if (_angryEffectInstance != null) return; // もうあるなら二重生成しない

        Transform follow = angryEffectFollowPoint ? angryEffectFollowPoint : this.transform;

        _angryEffectInstance = Instantiate(angryEffectPrefab, follow);
        _angryEffectInstance.transform.localPosition = angryEffectLocalOffset;

        // 回転はプレハブ依存のまま（横向き対策として弄らない）
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

        // ★state1 かつ 隠れ中は絶対に捕獲処理を発火させない
        if (fixedState == 1 && HideRef && HideRef.hide)
        {
            return;
        }

        // 以降：元々ここで捕獲処理しているならそのまま…
        // if (other.CompareTag("Player")) { …捕獲処理… }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!GuardCatchByState) return;

        // ★state1 かつ 隠れ中は絶対に捕獲処理を発火させない
        if (fixedState == 1 && HideRef && HideRef.hide)
        {
            return;
        }

        // if (other.CompareTag("Player")) { …捕獲処理… }
    }

    // ====== デバッグ可視化 ======
    private void OnDrawGizmosSelected()
    {
        if (!Player) return;
        Gizmos.color = isDiscovery ? Color.red : Color.cyan;
        Gizmos.DrawLine(transform.position, Player.position);
    }
}
