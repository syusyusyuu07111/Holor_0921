//���񂵂Ȃ���T���Č�������ǂ�������X�N���v�g�ł�==========================================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.VisualScripting;

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

    void Start()
    {
        surface.navMeshData = new NavMeshData(surface.agentTypeID);
        surface.AddData();
        surface.collectObjects = CollectObjects.All;
    }

    void Update()
    {
        IsPlayerHit();

        repathtimer += Time.deltaTime;
        if(repathtimer>repathInterval)
        {
            repathtimer = 0;
            surface.UpdateNavMesh(surface.navMeshData);

            Chase();
        }
        EnsureAgentOnNavMesh();

        if(agent.hasPath&&!agent.pathPending)
        {
            if(agent.remainingDistance<=StopDistance)
            {
                agent.isStopped = true;
                if (!isDiscovery) Invoke("TargetChange", WaitCount);
            }
        }
    }
    void Chase()
    {
        if (!agent.isOnNavMesh) return;
        if(NavMesh.SamplePosition(target.position,out var hit,maxdistance,NavMesh.AllAreas))
        {
            agent.ResetPath();
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }
        //�␳������------------------------------------------------------------------------------------------------------
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
    //�ړI�n�ɂ�����Ƀ^�[�Q�b�g��ς���----------------------------------------------------------------------------------
    void TargetChange()
    {
        if (!agent.isStopped) return;//�����Ă���^�[�Q�b�g�ς��Ȃ�
        CurrenTtargetNum++;//���̑ΏۂɌ����킹��
        if(targetlist.Count<=CurrenTtargetNum)
        {
            CurrenTtargetNum = 0;
        }
        target = targetlist[CurrenTtargetNum];
        agent.isStopped = false;
        Chase();
    }
    public void IsPlayerHit()
    {
        var _dir = Player.position - transform.position;
        if(Physics.Raycast(transform.position,_dir,out RaycastHit hit,10))
        {
            if(hit.collider.gameObject.CompareTag("Player"))
            {
                isDiscovery = true;
                lostposition.position = Player.position;
                target = lostposition;
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
}







