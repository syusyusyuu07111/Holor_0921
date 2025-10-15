//���񂵂Ȃ���T���Č�������ǂ�������X�N���v�g�ł�==========================================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.VisualScripting;
using UnityEngine.UI;                                     // UI.Text �p
using TMPro;                                             // TextMeshPro �p

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

    // --------------- �����_�����(1 or 2) ---------------
    [SerializeField] private int fixedState = 1;          // ���1��2�̂ǂ��炩�iStart�Ō���j
    public int GetState() => fixedState;                  // �l��Ԃ�

    // --------------- �B���ԎQ�� ---------------
    public HideCroset HideRef;                            // �v���C���[�ɕt���Ă��� HideCroset ���A�^�b�`

    // --------------- �f�o�b�O�\���i��ʃe�L�X�g�j ---------------
    public TextMeshProUGUI StateLabelTMP;                 // TextMeshPro �̎Q�Ɓi�ǂ��炩�Е�������OK�j
    public Text StateLabelLegacy;                         // ��UI.Text �̎Q��
    public string State1Text = "STATE: 1  �B��Ă���Ԃ͌�����Ȃ�"; // ���1�̕\������
    public string State2Text = "STATE: 2  �������Ă�������";        // ���2�̕\������

    void Start()
    {
        surface.navMeshData = new NavMeshData(surface.agentTypeID);
        surface.AddData();
        surface.collectObjects = CollectObjects.All;

        fixedState = Random.Range(1, 3);                  // 1 or 2�i����͔r���I�j
        UpdateStateLabel();                               // ��ʃe�L�X�g���X�V
    }

    void Update()
    {
        // --------------- ��Ԃŕ���i���g�͋�j ---------------
        if (fixedState == 1)
        {
            // �i���1�F�m�[�}���B�B��Ă��猩����Ȃ��j
        }
        if (fixedState == 2)
        {
            // �i���2�F�B��Ă��K��������j
        }

        IsPlayerHit();                                    // ��������i��ԃ��[�����ŗD��œK�p�j

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
        if (!agent.isStopped) return;                     // �����Ă���^�[�Q�b�g�ς��Ȃ�
        CurrenTtargetNum++;                               // ���̑ΏۂɌ����킹��
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
        // --------------- ��ԃ��W�b�N�i������/������Ȃ��̋����j ---------------
        if (fixedState == 1 && HideRef && HideRef.hide)
        {
            isDiscovery = false;                          // �B��Ă���Ԃ͐�΂Ɍ�����Ȃ�
            return;                                       // ���C������X�L�b�v
        }
        if (fixedState == 2)
        {
            isDiscovery = true;                           // �������Ă�������
            if (lostposition) lostposition.position = Player.position; // �ǐՊ���X�V
            if (lostposition) target = lostposition;      // �ǐՃ^�[�Q�b�g�ɐݒ�
            return;                                       // ���C������X�L�b�v
        }

        // --------------- �ʏ�̃��C����i���1�ŉB��Ă��Ȃ��ꍇ�j ---------------
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

    // --------------- �e�L�X�g�X�V ---------------
    private void UpdateStateLabel()
    {
        string msg = (fixedState == 1) ? State1Text : State2Text;   // �\�����b�Z�[�W��I��
        if (StateLabelTMP) StateLabelTMP.text = msg;                // TMP������ΗD��
        if (StateLabelLegacy) StateLabelLegacy.text = msg;          // ��Text������΂�����ɂ�
    }
}
