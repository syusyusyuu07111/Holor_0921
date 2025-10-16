// ���񂵂Ȃ���T���Č�������ǂ�������X�N���v�g
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;                 // TextMeshPro
using UnityEngine.UI;        // ��UI.Text

public class SearchChase : MonoBehaviour
{
    [Header("NavMesh/�Q��")]
    public NavMeshAgent agent;
    public NavMeshSurface surface;
    public Transform Player;
    public Transform target;
    public Transform lostposition;

    [Header("�o�H/����")]
    public float maxdistance = 2.0f;
    public float repathInterval = 0.25f;
    private float repathtimer = 0f;
    public float StopDistance = 0.5f;
    public float WaitCount = 2.0f;

    [Header("���m/�p�g���[��")]
    public bool isDiscovery = false;
    public List<Transform> targetlist = new List<Transform>();
    int CurrenTtargetNum = 0;

    // --------------- ���(1 or 2) ---------------
    [SerializeField] private int fixedState = 1;     // �����ێ�
    private bool _stateOverridden = false;           // �O����������������true
    public int GetState() => fixedState;

    // --------------- �B���ԎQ�� ---------------
    public HideCroset HideRef;

    // --------------- �f�o�b�O�\���i��ʃe�L�X�g�j ---------------
    [Header("�f�o�b�O�\��")]
    public TextMeshProUGUI StateLabelTMP;
    public Text StateLabelLegacy;
    public string State1Text = "STATE: 1  �B��Ă���Ԃ͌�����Ȃ�";
    public string State2Text = "STATE: 2  �������Ă�������";

    // ===== �O�������Ԃ��Œ肷��API�i�ŏd�v�j =====
    public void ForceState(int state)
    {
        fixedState = Mathf.Clamp(state, 1, 2);
        _stateOverridden = true;      // Start()�̃����_������𖳌���
        UpdateStateLabel();
    }

    void Start()
    {
        // NavMesh ����
        if (surface)
        {
            surface.navMeshData = new NavMeshData(surface.agentTypeID);
            surface.AddData();
            surface.collectObjects = CollectObjects.All;
        }

        // �� �O�����疢�w��̂Ƃ����������_��
        if (!_stateOverridden)
        {
            fixedState = Random.Range(1, 3); // 1 or 2�i����͔r���I�j
        }
        UpdateStateLabel();

        // �ǐՊ��̃`���[�j���O
        if (agent)
        {
            agent.stoppingDistance = 0f;  // �ǐՎ��Ɏ�O�Ŏ~�܂�Ȃ�
            agent.autoBraking = false;    // �����}��
        }
    }

    void Update()
    {
        // ��ԕʁi�K�v�Ȃ�ǉ��j
        if (fixedState == 1)
        {
            // ���1�F�B��Ă���Ό�����Ȃ�
        }
        else if (fixedState == 2)
        {
            // ���2�F�K��������
        }

        IsPlayerHit(); // ��������i��ԃ��[�����݁j

        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0f;
            if (surface) surface.UpdateNavMesh(surface.navMeshData);
            Chase();
        }
        EnsureAgentOnNavMesh();

        // �p�g���[����~/�ĊJ
        if (agent && agent.hasPath && !agent.pathPending)
        {
            if (isDiscovery)
            {
                agent.isStopped = false; // �ǐՒ��͓˂�����
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

        // ��ԃ��[�����ŗD��
        if (fixedState == 1 && HideRef && HideRef.hide)
        {
            isDiscovery = false; // �B��Ă���Ԃ͐�΂Ɍ�����Ȃ�
            return;
        }
        if (fixedState == 2)
        {
            isDiscovery = true;  // �������Ă�������
            if (lostposition) { lostposition.position = Player.position; target = lostposition; }
            return;
        }

        // �ʏ�̃��C����i���1�Ŗ��B�ꎞ�j
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
