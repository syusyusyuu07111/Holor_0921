using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public class ChaseController : MonoBehaviour
{
    public NavMeshAgent agent;
    public NavMeshSurface surface;
    public Transform target;

    private float repathtimer = 0f;
    public float repathinterval = 0.1f;

    public float maxDistance = 2f;

    public float placeOnMeshSearchRadius = 8f;
    public float minSampleRadius = 6f;

    private void Start()
    {
        //================
        // NavMeshAgentと競合するコンポーネントを無効化する
        // RigidbodyやCharacterControllerが有効だと
        // 移動がブレたり止まったりするため先に止める
        //================
        StopPhysicsComponents();

        //================
        // スポーン位置がNavMesh外だった場合の保険
        // メッシュ上にスナップさせる
        //================
        if (!EnsureOnNavMesh())
        {
            Debug.LogError("[ChaseController] NavMeshに乗せられませんでした。");
            return;
        }

        //================
        // Agentの初期値補正
        // speedやaccelerationが0だと動かないため
        // 未設定の場合のみ安全値を入れる
        //================
        SetupAgentDefaults();
    }

    private void Update()
    {
        //================
        // 毎フレーム追跡すると重いので
        // 一定間隔で再経路探索する
        //================
        repathtimer += Time.deltaTime;
        if (repathtimer <= repathinterval) return;

        repathtimer = 0f;

        //================
        // 落下やワープでNavMesh外に出た場合の保険処理
        //================
        if (!EnsureOnNavMesh())
        {
            Debug.LogError("[ChaseController] NavMesh外のため追跡停止。");
            return;
        }

        //================
        // プレイヤー追跡処理
        //================
        TargetChase();
    }

    /*
         プレイヤーをNavMesh上に投影して追跡する
         直接target.positionを使わないのは
         NavMesh外を指定すると経路計算が失敗するため
    */
    private void TargetChase()
    {
        if (agent == null)
        {
            Debug.LogError("[ChaseController] agent未設定。");
            return;
        }

        if (target == null)
        {
            Debug.LogError("[ChaseController] target未設定。");
            return;
        }

        if (!agent.isOnNavMesh) return;

        // 目標位置をNavMesh上に変換するための探索半径
        float SampleRadius = Mathf.Max(minSampleRadius, maxDistance);

        if (NavMesh.SamplePosition(target.position, out NavMeshHit Hit, SampleRadius, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(Hit.position);
            return;
        }

        // Sample失敗時は何もしない（前回の経路を維持）
    }

    /*
         NavMesh外にいる場合
         一番近いNavMeshへワープさせる
         MoveではなくWarpを使うのは
         Agent内部状態を正しく更新するため
    */
    private bool EnsureOnNavMesh()
    {
        if (agent == null)
        {
            Debug.LogError("[ChaseController] agent未設定。");
            return false;
        }

        if (agent.isOnNavMesh) return true;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit Hit, placeOnMeshSearchRadius, NavMesh.AllAreas))
        {
            agent.Warp(Hit.position);
            return true;
        }

        return false;
    }

    /*
         Rigidbody / CharacterControllerを止める
         NavMeshAgentは自前で移動制御するため
         他の移動系コンポーネントと共存させない
    */
    private void StopPhysicsComponents()
    {
        Rigidbody Rb = GetComponent<Rigidbody>();
        if (Rb != null)
        {
            Rb.isKinematic = true;
            Rb.useGravity = false;
        }

        CharacterController Cc = GetComponent<CharacterController>();
        if (Cc != null) Cc.enabled = false;
    }

    /*
         Agentの安全初期化
         speedやaccelerationが0だと動かないため補正する
         既に値が入っている場合は変更しない
    */
    private void SetupAgentDefaults()
    {
        if (agent == null)
        {
            Debug.LogError("[ChaseController] agent未設定。");
            return;
        }

        agent.isStopped = false;

        if (agent.speed <= 0f) agent.speed = 3.5f;
        if (agent.acceleration <= 0f) agent.acceleration = 8f;

        agent.autoBraking = false;
        agent.updatePosition = true;
        agent.updateRotation = true;
        agent.baseOffset = Mathf.Max(0.1f, agent.baseOffset);
    }
}