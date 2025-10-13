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
    public float repathinterval = 0.1f;   // 追跡頻度（細かめ）

    public float maxDistance = 2;         // SamplePosition の探索半径（従来変数は残す）

    //=== 追加：メッシュ外スポーンの保険 ===================================================
    public float placeOnMeshSearchRadius = 8f;   // 近くのNavMeshを探す半径
    public float minSampleRadius = 6f;           // 目標をNavMeshへ投影する最低半径

    void Start()
    {
        // 物理/コンポーネントの競合を止める -------------------------------------------------
        var rb = GetComponent<Rigidbody>();
        if (rb) { rb.isKinematic = true; rb.useGravity = false; }

        var cc = GetComponent<CharacterController>();
        if (cc) cc.enabled = false; // NavMeshAgent と同居しない

        // NavMesh上に載せる ----------------------------------------------------------------
        EnsureOnNavMesh();

        // Agentの基本値（未設定/0対策） ----------------------------------------------------
        if (agent)
        {
            agent.isStopped = false;
            if (agent.speed <= 0f) agent.speed = 3.5f;
            if (agent.acceleration <= 0f) agent.acceleration = 8f;
            agent.autoBraking = false;           // 減速で止まりにくくする
            agent.updatePosition = true;
            agent.updateRotation = true;
            agent.baseOffset = Mathf.Max(0.1f, agent.baseOffset); // わずかに浮かせる（沈み防止）
        }
    }

    // Update is called once per frame -------------------------------------------------------
    void Update()
    {
        repathtimer += Time.deltaTime;
        if (repathtimer > repathinterval)
        {
            repathtimer = 0f;

            // 毎回、NavMesh上にいるかだけ確認（スポーン直後/段差落下の保険）
            if (!EnsureOnNavMesh()) return;

            TargetChase(); // 常に追いかける
        }
    }

    // ターゲットを追いかける ---------------------------------------------------------------
    void TargetChase()
    {
        if (!agent || !target) return;
        if (!agent.isOnNavMesh) return;

        // 目標をNavMesh上にスナップ（半径が小さいと失敗しやすいので下限を用意）
        float sample = Mathf.Max(minSampleRadius, maxDistance);
        if (NavMesh.SamplePosition(target.position, out var hit, sample, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
        else
        {
            // Sampleに失敗した場合は前回の経路を維持（何もしない）
        }
    }

    // メッシュ外ならワープして載せる -------------------------------------------------------
    bool EnsureOnNavMesh()
    {
        if (!agent) return false;
        if (agent.isOnNavMesh) return true;

        if (NavMesh.SamplePosition(transform.position, out var hit, placeOnMeshSearchRadius, NavMesh.AllAreas))
        {
            agent.Warp(hit.position); // MoveではなくWarpでNavMeshへスナップ
            return true;
        }
        return false;
    }
}
