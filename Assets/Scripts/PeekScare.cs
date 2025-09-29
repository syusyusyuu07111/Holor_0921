using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class PeekScare : MonoBehaviour
{
    public Transform Ghost;
    public Transform Camera;

    public GameObject Goal;          // 追う対象（Player）
    public float distance;           // プレイヤーとの距離
    public NavMeshAgent agent;

    [Header("幽霊の動く情報")]
    public float Speed = 5f;
    public float StopDistance = 0.3f;

    // 追加：重複起動を防ぐ
    bool running = false;
    Coroutine scareCo;

    void Start()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!Goal) Goal = GameObject.Find("Player"); // 可能なら Inspector か Tag で渡すのが望ましい

        if (agent)
        {
            agent.speed = Speed;
            agent.stoppingDistance = StopDistance;
            agent.autoBraking = true;
            agent.updateRotation = true;
            agent.isStopped = true; // 初期は停止
        }
    }

    void Update()
    {
        bool peeking = (PeekCamera.Instance != null && PeekCamera.Instance.IsPeeking);

        // 覗き開始 → 追跡開始（1回だけ）
        if (peeking && !running)
        {
            running = true;
            agent.isStopped = false;
            scareCo = StartCoroutine(Scare());
        }
        // 覗き終了 → 追跡停止（進行中なら止める）
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
                // 追跡先を更新
                agent.SetDestination(Goal.transform.position);

                distance = Vector3.Distance(transform.position, Goal.transform.position);

                // 目の前まで来たら終了 演出ここに入れる
                if (distance <= StopDistance)
                {
                    agent.isStopped = true;
                    break;
                }
            }
            yield return null; // 次フレームまで待つ
        }

        running = false;
    }
}
