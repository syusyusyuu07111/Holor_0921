using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;

public class ChaseController : MonoBehaviour
{
    public NavMeshAgent agent;
    public NavMeshSurface surface;
    public Transform target;

    float repathtimer = 0f;
    public float repathinterval = 0.1f;   // �ǐՕp�x�i�ׂ��߁j

    public float maxDistance = 2;         // SamplePosition �̒T�����a�i�]���ϐ��͎c���j

    //=== �ǉ��F���b�V���O�X�|�[���̕ی� ===================================================
    public float placeOnMeshSearchRadius = 8f;   // �߂���NavMesh��T�����a
    public float minSampleRadius = 6f;           // �ڕW��NavMesh�֓��e����Œᔼ�a

    void Start()
    {
        // ����/�R���|�[�l���g�̋������~�߂� -------------------------------------------------
        var rb = GetComponent<Rigidbody>();
        if (rb) { rb.isKinematic = true; rb.useGravity = false; }

        var cc = GetComponent<CharacterController>();
        if (cc) cc.enabled = false; // NavMeshAgent �Ɠ������Ȃ�

        // NavMesh��ɍڂ��� ----------------------------------------------------------------
        EnsureOnNavMesh();

        // Agent�̊�{�l�i���ݒ�/0�΍�j ----------------------------------------------------
        if (agent)
        {
            agent.isStopped = false;
            if (agent.speed <= 0f) agent.speed = 3.5f;
            if (agent.acceleration <= 0f) agent.acceleration = 8f;
            agent.autoBraking = false;           // �����Ŏ~�܂�ɂ�������
            agent.updatePosition = true;
            agent.updateRotation = true;
            agent.baseOffset = Mathf.Max(0.1f, agent.baseOffset); // �킸���ɕ�������i���ݖh�~�j
        }
    }

    // Update is called once per frame -------------------------------------------------------
    void Update()
    {
        repathtimer += Time.deltaTime;
        if (repathtimer > repathinterval)
        {
            repathtimer = 0f;

            // ����ANavMesh��ɂ��邩�����m�F�i�X�|�[������/�i�������̕ی��j
            if (!EnsureOnNavMesh()) return;

            TargetChase(); // ��ɒǂ�������
        }
    }

    // �^�[�Q�b�g��ǂ������� ---------------------------------------------------------------
    void TargetChase()
    {
        if (!agent || !target) return;
        if (!agent.isOnNavMesh) return;

        // �ڕW��NavMesh��ɃX�i�b�v�i���a���������Ǝ��s���₷���̂ŉ�����p�Ӂj
        float sample = Mathf.Max(minSampleRadius, maxDistance);
        if (NavMesh.SamplePosition(target.position, out var hit, sample, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
        else
        {
            // Sample�Ɏ��s�����ꍇ�͑O��̌o�H���ێ��i�������Ȃ��j
        }
    }

    // ���b�V���O�Ȃ烏�[�v���čڂ��� -------------------------------------------------------
    bool EnsureOnNavMesh()
    {
        if (!agent) return false;
        if (agent.isOnNavMesh) return true;

        if (NavMesh.SamplePosition(transform.position, out var hit, placeOnMeshSearchRadius, NavMesh.AllAreas))
        {
            agent.Warp(hit.position); // Move�ł͂Ȃ�Warp��NavMesh�փX�i�b�v
            return true;
        }
        return false;
    }
}
