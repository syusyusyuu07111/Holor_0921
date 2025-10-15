// 巡回しながら探して見つけたら追いかけるスクリプト
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;                 // TextMeshPro
using UnityEngine.UI;        // 旧UI.Text

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

    // --------------- 状態(1 or 2) ---------------
    [SerializeField] private int fixedState = 1;     // 内部保持
    private bool _stateOverridden = false;           // 外部強制が入ったらtrue
    public int GetState() => fixedState;

    // --------------- 隠れ状態参照 ---------------
    public HideCroset HideRef;

    // --------------- デバッグ表示（画面テキスト） ---------------
    [Header("デバッグ表示")]
    public TextMeshProUGUI StateLabelTMP;
    public Text StateLabelLegacy;
    public string State1Text = "STATE: 1  隠れている間は見つからない";
    public string State2Text = "STATE: 2  何をしても見つかる";

    // ===== 外部から状態を固定するAPI（最重要） =====
    public void ForceState(int state)
    {
        fixedState = Mathf.Clamp(state, 1, 2);
        _stateOverridden = true;      // Start()のランダム決定を無効化
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

        // ★ 外部から未指定のときだけランダム
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
        // 状態別（必要なら追加）
        if (fixedState == 1)
        {
            // 状態1：隠れていれば見つからない
        }
        else if (fixedState == 2)
        {
            // 状態2：必ず見つかる
        }

        IsPlayerHit(); // 発見判定（状態ルール込み）

        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0f;
            if (surface) surface.UpdateNavMesh(surface.navMeshData);
            Chase();
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

    void Chase()
    {
        if (!agent || !agent.isOnNavMesh || !target) return;
        if (NavMesh.SamplePosition(target.position, out var hit, maxdistance, NavMesh.AllAreas))
        {
            agent.ResetPath();
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

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

    public void IsPlayerHit()
    {
        if (!Player)
        {
            isDiscovery = false;
            return;
        }

        // 状態ルールを最優先
        if (fixedState == 1 && HideRef && HideRef.hide)
        {
            isDiscovery = false; // 隠れている間は絶対に見つからない
            return;
        }
        if (fixedState == 2)
        {
            isDiscovery = true;  // 何をしても見つかる
            if (lostposition) { lostposition.position = Player.position; target = lostposition; }
            return;
        }

        // 通常のレイ判定（状態1で未隠れ時）
        var _dir = Player.position - transform.position;
        if (Physics.Raycast(transform.position, _dir, out RaycastHit hit, 10f))
        {
            if (hit.collider.gameObject.CompareTag("Player"))
            {
                isDiscovery = true;
                if (lostposition) { lostposition.position = Player.position; target = lostposition; }
            }
            else
            {
                isDiscovery = false;
            }
        }
        else
        {
            isDiscovery = false;
        }
    }

    private void UpdateStateLabel()
    {
        string msg = (fixedState == 1) ? State1Text : State2Text;
        if (StateLabelTMP) StateLabelTMP.text = msg;
        if (StateLabelLegacy) StateLabelLegacy.text = msg;
    }
}
