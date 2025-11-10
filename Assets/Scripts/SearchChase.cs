using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;
using UnityEngine.UI;
using System.Collections;

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
    private bool _foundPrev = false;

    [Header("状態(1=通常探索, 2=追跡モード)")]
    [SerializeField] private int fixedState = 2; // ← デフォルトを2に固定（捕獲可能）
    private bool _stateOverridden = false;
    public int GetState() => fixedState;
    public void ForceState(int state)
    {
        int prev = fixedState;
        fixedState = Mathf.Clamp(state, 1, 2);
        _stateOverridden = true;
        if (prev != fixedState)
            Debug.Log($"[SearchChase] 状態変更: {prev} → {fixedState}");
        UpdateStateLabel();
    }

    [Header("隠れ状態参照")]
    public HideCroset HideRef;

    [Header("デバッグ表示（任意）")]
    public TextMeshProUGUI StateLabelTMP;
    public Text StateLabelLegacy;
    [TextArea] public string State1Text = "STATE:1 隠れてる間は安全";
    [TextArea] public string State2Text = "STATE:2 クローゼット内でも発見可";

    [Header("見渡し設定")]
    public float LookInterval = 5f;
    public float LookDuration = 2f;
    public float LookAngle = 360f;
    private float lookTimer = 0f;
    private bool isLooking = false;

    [Header("アニメーション")]
    public Animator animator;
    public string ChaseBoolName = "Chase";

    [Header("怒りエフェクト")]
    public GameObject angryEffectPrefab;
    public Transform angryEffectFollowPoint;
    private GameObject _angryEffectInstance;
    public Vector3 angryEffectLocalOffset = Vector3.zero;

    [Header("捕獲ガード")]
    public bool GuardCatchByState = true;

    [Header("NavMesh更新")]
    public bool enableRuntimeNavmeshUpdate = false;

    [Header("視線(LoS)設定")]
    public float eyeHeight = 1.6f;
    public float playerHeight = 1.2f;
    public LayerMask losBlockMask = ~0;

    void Start()
    {
        if (surface)
        {
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.collectObjects = CollectObjects.All;
            surface.AddData();
        }

        if (!_stateOverridden)
            fixedState = Mathf.Clamp(fixedState, 1, 2);

        if (!HideRef)
            HideRef = FindFirstObjectByType<HideCroset>();

        if (agent)
        {
            agent.stoppingDistance = 0f;
            agent.autoBraking = false;
        }

        UpdateStateLabel();
    }

    void Update()
    {
        UpdateDiscovery();

        if (isDiscovery != _foundPrev)
        {
            if (isDiscovery)
            {
                Debug.Log("[SearchChase] プレイヤー発見");
                SetChaseAnim(true);
                SpawnAngryEffect();
            }
            else
            {
                Debug.Log("[SearchChase] 見失い");
                SetChaseAnim(false);
                DespawnAngryEffect();
            }
        }
        _foundPrev = isDiscovery;

        // 探索行動
        if (!isDiscovery)
        {
            lookTimer += Time.deltaTime;
            if (!isLooking && lookTimer >= LookInterval)
            {
                if (agent && agent.isOnNavMesh)
                    StartCoroutine(LookAround());
                else
                    lookTimer = 0f;
            }
        }
        else lookTimer = 0f;

        // 経路更新
        repathtimer += Time.deltaTime;
        if (repathtimer > repathInterval)
        {
            repathtimer = 0f;
            if (surface && enableRuntimeNavmeshUpdate)
                surface.UpdateNavMesh(surface.navMeshData);

            if (!isLooking)
                ChaseMove();
        }

        EnsureAgentOnNavMesh();
    }

    void ChaseMove()
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
        if (!agent.isOnNavMesh && NavMesh.SamplePosition(agent.transform.position, out var hit, 0.5f, NavMesh.AllAreas))
            agent.Warp(hit.position);
    }

    void TargetChange()
    {
        if (!agent) return;
        if (!agent.isStopped) return;
        CurrenTtargetNum++;
        if (targetlist.Count <= CurrenTtargetNum) CurrenTtargetNum = 0;
        if (targetlist.Count > 0) target = targetlist[CurrenTtargetNum];
        agent.isStopped = false;
        ChaseMove();
    }

    private void UpdateDiscovery()
    {
        if (!Player) { isDiscovery = false; return; }

        bool hidden = HideRef && HideRef.hide;
        switch (fixedState)
        {
            case 1:
                if (hidden)
                {
                    isDiscovery = false;
                    return;
                }
                break;

            case 2:
                if (hidden)
                {
                    isDiscovery = true;
                    UpdateLostAndTarget();
                    return;
                }
                break;
        }

        isDiscovery = HasLineOfSightToPlayer();
        if (isDiscovery) UpdateLostAndTarget();
    }

    private void UpdateLostAndTarget()
    {
        if (lostposition)
        {
            lostposition.position = Player.position;
            target = lostposition;
        }
        else target = Player;
    }

    private bool HasLineOfSightToPlayer()
    {
        if (!Player) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = Player.position + Vector3.up * playerHeight;
        Vector3 dir = targetPos - origin;
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return true;

        dir.Normalize();
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, losBlockMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.CompareTag("Player") || hit.collider.transform.root.CompareTag("Player"))
                return true;
            return false;
        }
        return true;
    }

    private void UpdateStateLabel()
    {
        string msg = (fixedState == 1) ? State1Text : State2Text;
        if (StateLabelTMP) StateLabelTMP.text = msg;
        if (StateLabelLegacy) StateLabelLegacy.text = msg;
    }

    IEnumerator LookAround()
    {
        isLooking = true;
        float elapsed = 0f;
        float turnSpeed = (LookDuration > 0f) ? (LookAngle / LookDuration) : 0f;
        CancelInvoke(nameof(TargetChange));

        if (agent)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.updateRotation = false;
        }

        while (elapsed < LookDuration)
        {
            UpdateDiscovery();
            if (isDiscovery) break;
            transform.Rotate(0f, turnSpeed * Time.deltaTime, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        isLooking = false;
        lookTimer = 0f;
        if (agent)
        {
            agent.updateRotation = true;
            agent.isStopped = false;
        }
        ChaseMove();
    }

    private void SetChaseAnim(bool chasing)
    {
        if (!animator || string.IsNullOrEmpty(ChaseBoolName)) return;
        animator.SetBool(ChaseBoolName, chasing);
    }

    private void SpawnAngryEffect()
    {
        if (!angryEffectPrefab || _angryEffectInstance) return;
        Transform follow = angryEffectFollowPoint ? angryEffectFollowPoint : this.transform;
        _angryEffectInstance = Instantiate(angryEffectPrefab, follow);
        _angryEffectInstance.transform.localPosition = angryEffectLocalOffset;
    }

    private void DespawnAngryEffect()
    {
        if (_angryEffectInstance)
        {
            Destroy(_angryEffectInstance);
            _angryEffectInstance = null;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!GuardCatchByState) return;
        if (fixedState == 1 && HideRef && HideRef.hide) return;
    }

    private void OnTriggerStay(Collider other)
    {
        if (!GuardCatchByState) return;
        if (fixedState == 1 && HideRef && HideRef.hide) return;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Player) return;
        Gizmos.color = isDiscovery ? Color.red : Color.cyan;
        Gizmos.DrawLine(transform.position + Vector3.up * eyeHeight,
                        Player.position + Vector3.up * playerHeight);
    }
}
