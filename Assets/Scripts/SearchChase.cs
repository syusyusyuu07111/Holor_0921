// 巡回しながら探して見つけたら追いかけるスクリプト
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;                 // TextMeshPro
using UnityEngine.UI;        // 旧UI.Text
using System.Collections;    // ← コルーチン用

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
    [SerializeField] private int fixedState = 1;
    private bool _stateOverridden = false;
    public int GetState() => fixedState;

    // --------------- 隠れ状態参照 ---------------
    public HideCroset HideRef;

    // --------------- デバッグ表示（画面テキスト） ---------------
    [Header("デバッグ表示")]
    public TextMeshProUGUI StateLabelTMP;
    public Text StateLabelLegacy;
    public string State1Text = "STATE: 1  隠れている間は見つからない";
    public string State2Text = "STATE: 2  何をしても見つかる";

    // ===== 見渡し（サーチ） =====
    [Header("見渡し（サーチ）")]
    public float LookInterval = 5.0f;    // 何秒ごとに見渡すか
    public float LookDuration = 2.0f;    // 何秒間見渡すか
    public float LookAngle = 360f;       // その間の総回転角（度）
    private float lookTimer = 0f;
    private bool isLooking = false;      // 見渡し中フラグ

    // ===== 外部から状態を固定するAPI（最重要） =====
    public void ForceState(int state)
    {
        fixedState = Mathf.Clamp(state, 1, 2);
        _stateOverridden = true;
        UpdateStateLabel();
    }

    void Start()
    {
        if (surface)
        {
            surface.navMeshData = new NavMeshData(surface.agentTypeID);
            surface.AddData();
            surface.collectObjects = CollectObjects.All;
        }

        if (!_stateOverridden)
        {
            fixedState = Random.Range(1, 3);
        }
        UpdateStateLabel();

        if (agent)
        {
            agent.stoppingDistance = 0f;
            agent.autoBraking = false;
        }
    }

    void Update()
    {
        if (fixedState == 1)
        {
            // 状態1
        }
        else if (fixedState == 2)
        {
            // 状態2
        }

        IsPlayerHit(); // 発見判定

        // ---- 見渡しトリガ ----
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
                    lookTimer = 0f;
                }
            }
        }
        else
        {
            lookTimer = 0f; // 発見中はリセット
        }

        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0f;
            if (surface) surface.UpdateNavMesh(surface.navMeshData);

            // ★見渡し中は経路更新で動かないように抑止
            if (!isLooking)
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
                agent.isStopped = false;
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

        if (fixedState == 1 && HideRef && HideRef.hide)
        {
            isDiscovery = false;
            return;
        }
        if (fixedState == 2)
        {
            isDiscovery = true;
            if (lostposition) { lostposition.position = Player.position; target = lostposition; }
            return;
        }

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

    // ===== 見渡しコルーチン（停止して回頭） =====
    IEnumerator LookAround()
    {
        isLooking = true;
        float elapsed = 0f;
        float turnSpeed = (LookDuration > 0f) ? (LookAngle / LookDuration) : 0f;

        CancelInvoke(nameof(TargetChange));

        if (agent)
        {
            agent.isStopped = true;            // その場で停止
            agent.ResetPath();                 // 経路破棄
            agent.velocity = Vector3.zero;     // 慣性を即座に殺す
            agent.updateRotation = false;      // 自動回頭OFF（手動で回す）
        }

        while (elapsed < LookDuration)
        {
            IsPlayerHit();
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

        // 現在のtargetへ復帰（発見していればlostpositionへ）
        Chase();
    }
}
