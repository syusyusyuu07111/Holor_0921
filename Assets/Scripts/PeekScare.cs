using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class PeekScare : MonoBehaviour
{
    public Transform Ghost;
    public Transform Camera;

    public GameObject Goal;          // �ǂ��ΏہiPlayer�j
    public float distance;           // �v���C���[�Ƃ̋���
    public NavMeshAgent agent;

    [Header("�H��̓������")]
    public float Speed = 5f;
    public float StopDistance = 0.3f;

    // �ǉ��F�d���N����h��
    bool running = false;
    Coroutine scareCo;

    void Start()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!Goal) Goal = GameObject.Find("Player"); // �\�Ȃ� Inspector �� Tag �œn���̂��]�܂���

        if (agent)
        {
            agent.speed = Speed;
            agent.stoppingDistance = StopDistance;
            agent.autoBraking = true;
            agent.updateRotation = true;
            agent.isStopped = true; // �����͒�~
        }
    }

    void Update()
    {
        bool peeking = (PeekCamera.Instance != null && PeekCamera.Instance.IsPeeking);

        // �`���J�n �� �ǐՊJ�n�i1�񂾂��j
        if (peeking && !running)
        {
            running = true;
            agent.isStopped = false;
            scareCo = StartCoroutine(Scare());
        }
        // �`���I�� �� �ǐՒ�~�i�i�s���Ȃ�~�߂�j
        else if (!peeking && running)
        {
            if (scareCo != null) StopCoroutine(scareCo);
            agent.isStopped = true;
            running = false;
        }
    }

    IEnumerator Scare()
    {
        while (PeekCamera.Instance != null && PeekCamera.Instance.IsPeeking)
        {
            if (Goal != null && agent != null)
            {
                // �ǐՐ���X�V
                agent.SetDestination(Goal.transform.position);

                distance = Vector3.Distance(transform.position, Goal.transform.position);

                // �ڂ̑O�܂ŗ�����I�� ���o�����ɓ����
                if (distance <= StopDistance)
                {
                    agent.isStopped = true;
                    break;
                }
            }
            yield return null; // ���t���[���܂ő҂�
        }

        running = false;
    }
}
