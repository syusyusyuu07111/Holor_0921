//巡回しながら探して見つけたら追いかけるスクリプトです==========================================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.VisualScripting;
using UnityEngine.UI;                                     // UI.Text 用
using TMPro;                                             // TextMeshPro 用

public class SearchChase : MonoBehaviour
{
    public NavMeshAgent agent;
    public NavMeshSurface surface;
    public Transform Player;
    public Transform target;
    public Transform lostposition;

    public float maxdistance = 2.0f;
    public float repathInterval = 0.25f;
    float repathtimer = 0;
    public float StopDistance = 0.5f;
    public float WaitCount = 2.0f;

    public bool isDiscovery = false;

    public List<Transform> targetlist = new List<Transform>();
    int CurrenTtargetNum = 0;

    // --------------- ランダム状態(1 or 2) ---------------
    [SerializeField] private int fixedState = 1;          // 常に1か2のどちらか（Startで決定）
    public int GetState() => fixedState;                  // 値を返す

    // --------------- 隠れ状態参照 ---------------
    public HideCroset HideRef;                            // プレイヤーに付いている HideCroset をアタッチ

    // --------------- デバッグ表示（画面テキスト） ---------------
    public TextMeshProUGUI StateLabelTMP;                 // TextMeshPro の参照（どちらか片方だけでOK）
    public Text StateLabelLegacy;                         // 旧UI.Text の参照
    public string State1Text = "STATE: 1  隠れている間は見つからない"; // 状態1の表示文言
    public string State2Text = "STATE: 2  何をしても見つかる";        // 状態2の表示文言

    void Start()
    {
        surface.navMeshData = new NavMeshData(surface.agentTypeID);
        surface.AddData();
        surface.collectObjects = CollectObjects.All;

        fixedState = Random.Range(1, 3);                  // 1 or 2（上限は排他的）
        UpdateStateLabel();                               // 画面テキストを更新
    }

    void Update()
    {
        // --------------- 状態で分岐（中身は空） ---------------
        if (fixedState == 1)
        {
            // （状態1：ノーマル。隠れてたら見つからない）
        }
        if (fixedState == 2)
        {
            // （状態2：隠れても必ず見つかる）
        }

        IsPlayerHit();                                    // 発見判定（状態ルールを最優先で適用）

        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0;
            surface.UpdateNavMesh(surface.navMeshData);
            Chase();
        }
        EnsureAgentOnNavMesh();

        if (agent.hasPath && !agent.pathPending)
        {
            if (agent.remainingDistance <= StopDistance)
            {
                agent.isStopped = true;
                if (!isDiscovery) Invoke("TargetChange", WaitCount);
            }
        }
    }

    void Chase()
    {
        if (!agent.isOnNavMesh) return;
        if (NavMesh.SamplePosition(target.position, out var hit, maxdistance, NavMesh.AllAreas))
        {
            agent.ResetPath();
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    //補正をする------------------------------------------------------------------------------------------------------
    void EnsureAgentOnNavMesh()
    {
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(agent.transform.position, out var hit, 0.5f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }
    }

    //目的地についた後にターゲットを変える----------------------------------------------------------------------------------
    void TargetChange()
    {
        if (!agent.isStopped) return;                     // 動いてたらターゲット変えない
        CurrenTtargetNum++;                               // 次の対象に向かわせる
        if (targetlist.Count <= CurrenTtargetNum)
        {
            CurrenTtargetNum = 0;
        }
        target = targetlist[CurrenTtargetNum];
        agent.isStopped = false;
        Chase();
    }

    public void IsPlayerHit()
    {
        // --------------- 状態ロジック（見つける/見つからないの強制） ---------------
        if (fixedState == 1 && HideRef && HideRef.hide)
        {
            isDiscovery = false;                          // 隠れている間は絶対に見つからない
            return;                                       // レイ判定をスキップ
        }
        if (fixedState == 2)
        {
            isDiscovery = true;                           // 何をしても見つかる
            if (lostposition) lostposition.position = Player.position; // 追跡基準を更新
            if (lostposition) target = lostposition;      // 追跡ターゲットに設定
            return;                                       // レイ判定をスキップ
        }

        // --------------- 通常のレイ判定（状態1で隠れていない場合） ---------------
        var _dir = Player.position - transform.position;
        if (Physics.Raycast(transform.position, _dir, out RaycastHit hit, 10))
        {
            if (hit.collider.gameObject.CompareTag("Player"))
            {
                isDiscovery = true;
                if (lostposition) lostposition.position = Player.position;
                if (lostposition) target = lostposition;
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

    // --------------- テキスト更新 ---------------
    private void UpdateStateLabel()
    {
        string msg = (fixedState == 1) ? State1Text : State2Text;   // 表示メッセージを選択
        if (StateLabelTMP) StateLabelTMP.text = msg;                // TMPがあれば優先
        if (StateLabelLegacy) StateLabelLegacy.text = msg;          // 旧Textがあればこちらにも
    }
}
