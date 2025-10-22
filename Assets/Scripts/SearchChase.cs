// 巡回しながら探して、見つけたら追いかけるスクリプト（LoS 必須版）
// state1: 隠れていれば必ず未発見。隠れていなければ LoS が通れば発見
// state2: 隠れていても発見するが、LoS が通らないと未発見（遮蔽物があれば未発見）

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;                 // TextMeshPro
using UnityEngine.UI;        // 旧UI.Text
using System.Collections;    // コルーチン用

public class SearchChase : MonoBehaviour
{
    [Header("NavMesh/参照")]
    public NavMeshAgent agent;
    public NavMeshSurface surface;
    public Transform Player;
    public Transform target;
    public Transform lostposition;

    [Header("経路/挙動")]
    public float maxdistance = 2.0f;          // NavMesh.SamplePosition の半径
    public float repathInterval = 0.25f;      // 経路再計算間隔
    private float repathtimer = 0f;
    public float StopDistance = 0.5f;         // パトロール時、到達判定距離
    public float WaitCount = 2.0f;            // 次ウェイポイントへ移る前の待機秒

    [Header("検知/パトロール")]
    public bool isDiscovery = false;          // 現在の発見状態
    public List<Transform> targetlist = new List<Transform>();
    int CurrenTtargetNum = 0;

    // 発見切り替えログ用
    private bool _foundPrev = false;

    // --------------- 状態(1 or 2) ---------------
    [SerializeField] private int fixedState = 1;     // 内部保持
    private bool _stateOverridden = false;           // 外部強制が入ったら true
    public int GetState() => fixedState;

    // --------------- 隠れ状態参照 ---------------
    public HideCroset HideRef;

    // --------------- デバッグ表示（画面テキスト） ---------------
    [Header("デバッグ表示")]
    public TextMeshProUGUI StateLabelTMP;
    public Text StateLabelLegacy;
    [TextArea] public string State1Text = "STATE: 1  隠れている間は見つからない（LoS必須）";
    [TextArea] public string State2Text = "STATE: 2  隠れていても見つかる（LoS必須）";

    // ===== 見渡し（サーチ） =====
    [Header("見渡し（サーチ）")]
    public float LookInterval = 5.0f;    // 何秒ごとに見渡すか
    public float LookDuration = 2.0f;    // 何秒間見渡すか
    public float LookAngle = 360f;       // その間の総回転角（度）
    private float lookTimer = 0f;
    private bool isLooking = false;      // 見渡し中フラグ

    // ===== 外部から状態を固定するAPI =====
    public void ForceState(int state)
    {
        fixedState = Mathf.Clamp(state, 1, 2);
        _stateOverridden = true;      // Start() のランダム決定を無効化
        UpdateStateLabel();
    }

    void Start()
    {
        // NavMesh 準備
        if (surface)
        {
            surface.navMeshData = new NavMeshData(surface.agentTypeID);
            surface.AddData();
            surface.collectObjects = CollectObjects.All;
        }

        // 外部から未指定のときだけランダム
        if (!_stateOverridden)
        {
            fixedState = Random.Range(1, 3); // 1 or 2（上限は排他的）
        }
        UpdateStateLabel();

        // 追跡寄りのチューニング
        if (agent)
        {
            agent.stoppingDistance = 0f;  // 追跡時に手前で止まらない
            agent.autoBraking = false;    // 減速抑制
        }
    }

    void Update()
    {
        // 発見判定
        UpdateDiscovery();

        // 切り替わりログ（false→true の立ち上がりのみ）
        if (isDiscovery && !_foundPrev)
        {
            Debug.Log("見つかってる状態");
        }
        _foundPrev = isDiscovery;

        // 見渡し（未発見の時だけ）
        if (!isDiscovery)
        {
            lookTimer += Time.deltaTime;
            if (!isLooking && lookTimer >= LookInterval)
            {
                if (agent && agent.isOnNavMesh)
                {
                    StartCoroutine(LookAround());
                }
                else
                {
                    lookTimer = 0f; // NavMesh外ならやり直し
                }
            }
        }
        else
        {
            lookTimer = 0f; // 発見中はリセット
        }

        // 経路更新
        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0f;
            if (surface) surface.UpdateNavMesh(surface.navMeshData);

            if (!isLooking) // 見渡し中は動かない
            {
                Chase();
            }
        }
        EnsureAgentOnNavMesh();

        // パトロール停止/再開
        if (agent && agent.hasPath && !agent.pathPending)
        {
            if (isDiscovery)
            {
                agent.isStopped = false; // 追跡中は突っ込む
            }
            else
            {
                if (agent.remainingDistance <= StopDistance)
                {
                    agent.isStopped = true;
                    Invoke(nameof(TargetChange), WaitCount);
                }
            }
        }
    }

    // ========== 追跡 ==========
    void Chase()
    {
        if (!agent || !agent.isOnNavMesh || !target) return;

        // 追跡ターゲットは「発見中は lostposition（最新のプレイヤー位置）」、未発見なら現在のウェイポイント
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
            {
                agent.Warp(hit.position);
            }
        }
    }

    // ========== パトロールの次点 ==========
    void TargetChange()
    {
        if (!agent) return;
        if (!agent.isStopped) return;

        CurrenTtargetNum++;
        if (targetlist.Count <= CurrenTtargetNum) CurrenTtargetNum = 0;
        if (targetlist.Count > 0) target = targetlist[CurrenTtargetNum];

        agent.isStopped = false;
        Chase();
    }

    // ========== 発見ロジック（LoS 必須） ==========
    private void UpdateDiscovery()
    {
        if (!Player)
        {
            isDiscovery = false;
            return;
        }

        // state1: 隠れていたら未発見
        if (fixedState == 1 && HideRef && HideRef.hide)
        {
            isDiscovery = false;
            return;
        }

        // state1/2 ともに LoS が通らなければ未発見
        isDiscovery = HasLineOfSightToPlayer();

        // 発見したら追跡ターゲットを最新位置へ
        if (isDiscovery) UpdateLostAndTarget();
    }

    private void UpdateLostAndTarget()
    {
        if (lostposition)
        {
            lostposition.position = Player.position;
            target = lostposition;
        }
        else
        {
            target = Player; // フォールバック
        }
    }

    // プレイヤーへの視線が遮られていないかを Raycast で確認
    private bool HasLineOfSightToPlayer()
    {
        Vector3 origin = transform.position;
        Vector3 dir = (Player.position - origin);
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return true;

        dir /= dist; // 正規化

        // 最初に当たったコライダが Player なら視線が通っていると判定
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist))
        {
            return hit.collider.CompareTag("Player");
        }
        // 何にも当たらなかった＝障害物なしで距離内 → 見えている扱い
        return true;
    }

    // 画面デバッグ用ラベル更新
    private void UpdateStateLabel()
    {
        string msg = (fixedState == 1) ? State1Text : State2Text;
        if (StateLabelTMP) StateLabelTMP.text = msg;
        if (StateLabelLegacy) StateLabelLegacy.text = msg;
    }

    // ===== 見渡しコルーチン（停止して回頭） =====
    IEnumerator LookAround()
    {
        isLooking = true;
        float elapsed = 0f;
        float turnSpeed = (LookDuration > 0f) ? (LookAngle / LookDuration) : 0f;

        // ウェイポイント切替の Invoke が重ならないように
        CancelInvoke(nameof(TargetChange));

        if (agent)
        {
            agent.isStopped = true;            // その場で停止
            agent.ResetPath();                 // 経路破棄
            agent.velocity = Vector3.zero;     // 慣性を即座に殺す
            agent.updateRotation = false;      // 自動回頭OFF（手動で回す）
        }

        // その間も発見判定は継続（見つけたら即中断）
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
            agent.updateRotation = true;       // 復帰
            agent.isStopped = false;           // 再開
        }

        // 現在の target へ復帰（発見していれば lostposition へ）
        Chase();
    }

    // ===== デバッグ可視化 =====
    private void OnDrawGizmosSelected()
    {
        if (!Player) return;
        Gizmos.color = isDiscovery ? Color.red : Color.cyan;
        Gizmos.DrawLine(transform.position, Player.position);
    }
}
