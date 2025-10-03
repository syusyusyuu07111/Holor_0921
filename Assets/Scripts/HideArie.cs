using System.Collections;
using TMPro;
using UnityEngine;

// キャラクターが３つ目の部屋に入った時に幽霊が出てくるから急いで隠れるギミックです============================================================================
public class HideArie : MonoBehaviour
{
    public AudioSource audioSource = null;
    public AudioClip KnockSe;
    public AudioClip KnockVoice;

    public bool Hide = false;                // 隠れているか
    public GameObject HidePlace;             // 隠れる場所
    InputSystem_Actions input;
    public float HideDistance;               // 隠れる判定距離
    public GameObject Player;                // プレイヤー
    public TextMeshProUGUI text;             // 「隠れる」ガイダンス
    public Transform HidePosition;           // 隠れたときに移動させる位置

    public float AttackWaitTime = 10.0f;     // 侵入後、襲いに来るまでの待ち時間
    public GameObject Ghost;                 // 幽霊のプレハブ
    public GameObject Door;                  // 出現位置などに使う参照

    public float GhostSpeed = 2.0f;          // 追跡時の移動速度
    public float GhostStopDistance = 0.2f;   // プレイヤーに近づきすぎたら停止する距離
    public float GhostLifetime = 10f;        // 幽霊の寿命（出現からこの秒数で消える）

    // カメラ（Display切替は使わず、enabled の切替のみ）
    public Camera MainCamera;                // 通常時に使うカメラ
    public Camera SubCamera;                 // 隠れている間に使うカメラ（常に幽霊の方向を向く）

    // 一度起動したら二度と起動しないためのフラグ
    private bool started = false;

    // 隠れる前のプレイヤー位置/回転を記録
    private Vector3 savedPlayerPos;
    private Quaternion savedPlayerRot;
    private bool savedPlayerValid = false;

    // 隠れ位置からこの距離を超えて左右(平面)に動いたら自動解除
    public float AutoExitDistance = 0.6f;

    // 生成した幽霊の参照
    private GameObject currentghost;

    [Header("Disappear Animation")]
    public string DisappearBoolName = "IsDisappearing";           // Animator Bool
    public string DisappearStateTag = "GhostDisappearStateTag";   // 消えるステートのTag

    //  インタラクトの連打で“入って即解除”を防ぐクールダウン
    private float interactCooldownUntil = 0f; // この時刻までは解除判定を無視

    // 二重生成防止フラグ
    private bool isSpawningGhost = false;

    // 消え処理（VFX待ち）中フラグ
    private bool isEnding = false;

    // 統合: Enemy2 の徘徊パラメータ（隠れている間の探索挙動） ==========================
    [Header("Wander (Enemy2 style)")]
    [SerializeField] private float _playerBaseSpeed = 5f; // プレイヤー基準速度
    [SerializeField] private float _enemySpeed = 1.3f;    // 速度倍率（徘徊用）
    [SerializeField] private float _rotationSpeed = 180f; // 旋回速度(度/秒)
    [SerializeField] private float _rayDistance = 1.5f;   // 前方レイの距離
    [Tooltip("家具など障害物のタグ名（Enemy2は\"Furniture\"を使用）")]
    public string ObstacleTag = "Furniture";
    // ================================================================================

    // コルーチン参照と前回のHide状態
    Coroutine followCo;
    Coroutine wanderCo;
    bool prevHide = false;

    [Header("VFX")]
    public GameObject GhostDisappearVfx;   // 消えるときに出すエフェクト（任意）
    public float VfxLifetime = 1.5f;       // VFXが完全に終わるまでの目安（Particleが無ければこれで待つ）

    // --------------------------------------------------------------------------------
    // ここから【追加：怒り（追跡）エフェクト】
    [Header("Angry (Chasing) Effect")]
    [Tooltip("追跡中にゴーストの頭上などに出すエフェクトのPrefab（ParticleSystem推奨）")]
    public GameObject AngryEffectPrefab;

    [Tooltip("怒りエフェクトのオフセット（ゴーストのローカル座標）")]
    public Vector3 AngryEffectLocalOffset = new Vector3(0f, 1.6f, 0f);

    [Tooltip("true=ゴーストの子にして追従（軽い） / false=Updateで毎フレーム追従（指示通り）")]
    public bool ParentEffectToGhost = true;

    [Tooltip("追跡中のみ表示。徘徊中は非表示にする")]
    public bool ShowOnlyWhileChasing = true;

    private Transform angryFx;             // インスタンス
    private ParticleSystem angryFxPs;      // キャッシュ
    // ここまで【追加】
    // --------------------------------------------------------------------------------

    private void Awake()
    {
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }

    private void Start()
    {
        if (text) text.gameObject.SetActive(false);

        if (MainCamera) { MainCamera.enabled = true; }
        if (SubCamera) { SubCamera.enabled = false; }

        var ml = MainCamera ? MainCamera.GetComponent<AudioListener>() : null;
        var sl = SubCamera ? SubCamera.GetComponent<AudioListener>() : null;
        if (ml) ml.enabled = true;
        if (sl) sl.enabled = false;
    }

    void Update()
    {
        // 隠れる場所に近づいたらテキスト表示／隠れる処理
        if (Player && HidePlace)
        {
            float distance = Vector3.Distance(Player.transform.position, HidePlace.transform.position);

            if (distance < HideDistance)
            {
                if (text) text.gameObject.SetActive(true);

                if (input.Player.Interact.WasPressedThisFrame() && !Hide)
                {
                    if (!savedPlayerValid)
                    {
                        savedPlayerPos = Player.transform.position;
                        savedPlayerRot = Player.transform.rotation;
                        savedPlayerValid = true;
                    }

                    if (HidePosition) Player.transform.position = HidePosition.position;
                    Hide = true;

                    if (text && !text.gameObject.activeSelf) text.gameObject.SetActive(true);

                    SwitchToSubCamera();

                    interactCooldownUntil = Time.time + 0.15f;
                }
            }
            else
            {
                if (!Hide && text) text.gameObject.SetActive(false);
            }
        }

        // 隠れている最中
        if (Hide)
        {
            if (SubCamera && currentghost) SubCamera.transform.LookAt(currentghost.transform.position);

            if (text && !text.gameObject.activeSelf) text.gameObject.SetActive(true);

            if (Time.time >= interactCooldownUntil && input.Player.Interact.WasPressedThisFrame())
            {
                ExitHide(false);
                interactCooldownUntil = Time.time + 0.15f;
            }

            if (HidePosition && Player)
            {
                Vector3 p = Player.transform.position; p.y = 0f;
                Vector3 h = HidePosition.position; h.y = 0f;
                if (Vector3.Distance(p, h) > AutoExitDistance)
                {
                    ExitHide(false);
                    interactCooldownUntil = Time.time + 0.15f;
                }
            }
        }

        // Hide の切替時に 追跡↔徘徊 を入れ替え
        if (currentghost != null)
        {
            if (Hide && !prevHide)
            {
                StopFollow();
                StartWander();
                // 追跡エフェクトは消す/止める
                if (ShowOnlyWhileChasing) HideAngryFx();
            }
            else if (!Hide && prevHide)
            {
                StopWander();
                StartFollow();
                // 追跡エフェクトを再開
                ShowAngryFx();
            }
        }
        prevHide = Hide;

        // -----------------------------------------
        // ここから【追加：エフェクト追従（非親子の場合）＆寿命監視】
        if (angryFx)
        {
            if (!ParentEffectToGhost && currentghost)
            {
                angryFx.position = currentghost.transform.TransformPoint(AngryEffectLocalOffset);
                angryFx.rotation = currentghost.transform.rotation;
            }
        }
        else
        {
            // ゴースト消滅に伴う後始末の取りこぼし防止
            angryFxPs = null;
        }
        // ここまで【追加】
        // -----------------------------------------
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Player")
        {
            if (!started)
            {
                started = true;
                StartCoroutine(Encount());

                if (audioSource && KnockSe) audioSource.PlayOneShot(KnockSe);

                var col = GetComponent<Collider>();
                if (col) col.enabled = false;
            }
        }
    }

    IEnumerator Encount()
    {
        // KnockSe 再生後、何があっても AttackWaitTime 後に必ず出現させる
        yield return new WaitForSeconds(AttackWaitTime);

        // 生成時点で追跡するかを決める（この瞬間 Hide なら追跡しない）
        bool chaseOnSpawn = !Hide;

        SpawnGhostIfNeeded(true, chaseOnSpawn);
    }

    IEnumerator GhostLifetimeRoutine()
    {
        yield return new WaitForSeconds(GhostLifetime);

        if (currentghost != null)
        {
            StartDisappear(currentghost);
        }
    }

    void StartDisappear(GameObject g)
    {
        if (g == null) return;

        isEnding = true;

        var anim = g.GetComponent<Animator>();
        if (anim != null && !string.IsNullOrEmpty(DisappearBoolName))
        {
            anim.SetBool(DisappearBoolName, true);
        }

        // 消え始めたら怒りエフェクトは消す
        HideAngryFx(true);

        StartCoroutine(WaitDisappearAndDestroy(g, anim));
    }

    IEnumerator WaitDisappearAndDestroy(GameObject g, Animator anim)
    {
        const int layer = 0;
        float enterSafety = 5f;       // タグのステートに入るまでの最大待機
        float stateLen = 0f;          // 実時間のステート長（speed補正後）
        float stateEnterTime = 0f;    // ステートに入った時刻（Time.time）

        GameObject vfx = null;        // 生成したVFXを保持

        if (anim != null && !string.IsNullOrEmpty(DisappearStateTag))
        {
            float t = 0f;
            while (t < enterSafety)
            {
                var info = anim.GetCurrentAnimatorStateInfo(layer);
                if (info.IsTag(DisappearStateTag))
                {
                    float speed = Mathf.Max(0.0001f, anim.speed);
                    stateLen = info.length / speed;
                    stateEnterTime = Time.time;
                    break;
                }
                t += Time.deltaTime;
                yield return null;
            }

            if (stateLen <= 0f)
            {
                stateLen = 1.0f;
                stateEnterTime = Time.time;
            }

            float untilVfx = Mathf.Max(0f, stateLen - 1f);
            float elapsed = Time.time - stateEnterTime;
            float waitToVfx = Mathf.Max(0f, untilVfx - elapsed);
            if (waitToVfx > 0f) yield return new WaitForSeconds(waitToVfx);

            Vector3 vfxPos = g.transform.position;
            Quaternion vfxRot = g.transform.rotation;
            if (GhostDisappearVfx != null)
            {
                vfx = Instantiate(GhostDisappearVfx, vfxPos, vfxRot);
                if (VfxLifetime > 0f) Destroy(vfx, VfxLifetime);
            }

            float remaining = Mathf.Max(0f, (stateLen - (Time.time - stateEnterTime)));
            if (remaining > 0f) yield return new WaitForSeconds(remaining);
        }
        else
        {
            if (GhostDisappearVfx != null)
            {
                vfx = Instantiate(GhostDisappearVfx, g.transform.position, g.transform.rotation);
                if (VfxLifetime > 0f) Destroy(vfx, VfxLifetime);
            }
            yield return new WaitForSeconds(1.0f);
        }

        if (g != null) Destroy(g);
        currentghost = null;

        float vfxWait = GetVfxRemainTime(vfx);
        if (vfxWait > 0f) yield return new WaitForSeconds(vfxWait);

        OnGhostEnd();
        isEnding = false;
    }

    float GetVfxRemainTime(GameObject vfx)
    {
        if (vfx == null) return 0f;

        var pss = vfx.GetComponentsInChildren<ParticleSystem>(true);
        float maxDur = 0f;
        foreach (var ps in pss)
        {
            var main = ps.main;
            float dur = main.duration;

            float life = 0f;
            if (main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants)
                life = Mathf.Max(main.startLifetime.constantMin, main.startLifetime.constantMax);
            else if (main.startLifetime.mode == ParticleSystemCurveMode.TwoCurves)
            {
                float max1 = (main.startLifetime.curveMax.keys.Length > 0)
                    ? main.startLifetime.curveMax.keys[main.startLifetime.curveMax.length - 1].time : 0f;
                float max2 = (main.startLifetime.curveMin.keys.Length > 0)
                    ? main.startLifetime.curveMin.keys[main.startLifetime.curveMin.length - 1].time : 0f;
                life = Mathf.Max(max1, max2);
            }
            else if (main.startLifetime.mode == ParticleSystemCurveMode.Curve)
            {
                life = (main.startLifetime.curve.keys.Length > 0)
                    ? main.startLifetime.curve.keys[main.startLifetime.curve.length - 1].time : 0f;
            }
            else
                life = main.startLifetime.constant;

            float total = dur + life;
            if (main.loop) { total = VfxLifetime > 0 ? VfxLifetime : 0f; }
            if (total > maxDur) maxDur = total;
        }

        if (maxDur <= 0f) maxDur = Mathf.Max(0f, VfxLifetime);
        return maxDur;
    }

    IEnumerator FollowGhost()
    {
        while (currentghost != null && Player != null && !Hide)
        {
            Vector3 to = Player.transform.position - currentghost.transform.position;
            float dist = to.magnitude;

            if (dist > GhostStopDistance)
            {
                Vector3 dir = to.normalized;
                currentghost.transform.position += dir * Time.deltaTime * GhostSpeed;

                if (dir.sqrMagnitude > 0.0001f)
                {
                    currentghost.transform.rotation = Quaternion.Slerp(
                        currentghost.transform.rotation,
                        Quaternion.LookRotation(dir, Vector3.up),
                        10f * Time.deltaTime
                    );
                }
            }
            yield return null;
        }

        if (currentghost == null)
        {
            if (!isEnding)
            {
                OnGhostEnd();
            }
        }
    }

    // 統合: Enemy2 の徘徊ロジック（隠れている間だけ動作） =====================================================================================
    IEnumerator WanderGhost()
    {
        if (currentghost == null) yield break;

        Rigidbody rb = currentghost.GetComponent<Rigidbody>();
        float speed = _playerBaseSpeed * _enemySpeed;
        bool isRotating = false;
        int turnDirection = 1;
        Quaternion targetRot = currentghost.transform.rotation;

        while (currentghost != null && Hide)
        {
            if (isRotating)
            {
                currentghost.transform.rotation =
                    Quaternion.RotateTowards(currentghost.transform.rotation, targetRot, _rotationSpeed * Time.deltaTime);

                if (Quaternion.Angle(currentghost.transform.rotation, targetRot) < 1f)
                {
                    isRotating = false;
                }

                yield return null;
                continue;
            }

            RaycastHit hit;
            if (Physics.Raycast(currentghost.transform.position,
                                currentghost.transform.forward,
                                out hit, _rayDistance))
            {
                if (hit.collider && hit.collider.CompareTag(ObstacleTag))
                {
                    turnDirection = (Random.Range(0, 2) == 0) ? 1 : -1;
                    Vector3 side = Vector3.Cross(currentghost.transform.forward, Vector3.up).normalized * turnDirection;
                    targetRot = Quaternion.LookRotation(side, Vector3.up);
                }
                else
                {
                    Vector3 side = Vector3.Cross(currentghost.transform.forward, Vector3.up).normalized *
                                   ((Random.Range(0, 2) == 0) ? 1 : -1);
                    targetRot = Quaternion.LookRotation(side, Vector3.up);
                }

                isRotating = true;
                yield return null;
                continue;
            }

            Vector3 nextPos = currentghost.transform.position + currentghost.transform.forward * speed * Time.deltaTime;
            if (rb != null)
            {
                rb.MovePosition(nextPos);
            }
            else
            {
                currentghost.transform.position = nextPos;
            }

            yield return null;
        }
    }
    // ==============================================================================

    void OnGhostEnd()
    {
        StopFollow();
        StopWander();
        HideAngryFx(true);    // 念のため消す
        ExitHide(false);
    }

    void ExitHide(bool _notUsedRespawn)
    {
        StopFollow();
        StopWander();
        HideAngryFx(true);

        Hide = false;

        if (Player && savedPlayerValid)
        {
            Player.transform.SetPositionAndRotation(savedPlayerPos, savedPlayerRot);
            savedPlayerValid = false;
        }

        SwitchToMainCamera();
    }

    // ゴースト生成を一元管理。生成時点で追跡させるか（chaseOnSpawn）を指定
    void SpawnGhostIfNeeded(bool fromEncount, bool chaseOnSpawn)
    {
        if (currentghost != null) return;
        if (isSpawningGhost) return;
        if (!Ghost || !Door) return;

        StartCoroutine(SpawnGhostRoutine(fromEncount, chaseOnSpawn));
    }

    IEnumerator SpawnGhostRoutine(bool fromEncount, bool chaseOnSpawn)
    {
        isSpawningGhost = true;
        yield return null;

        if (currentghost == null && Ghost && Door)
        {
            currentghost = Instantiate(Ghost, Door.transform.position, Quaternion.identity);

            if (audioSource && KnockVoice && !fromEncount)
            {
                audioSource.PlayOneShot(KnockVoice);
            }

            // 生成直後に怒りエフェクトを接続（追跡なら表示、徘徊なら隠す）
            EnsureAngryFxAttached();
            if (chaseOnSpawn) ShowAngryFx();
            else HideAngryFx();

            StartCoroutine(GhostLifetimeRoutine());

            if (chaseOnSpawn) { StartFollow(); } else { StartWander(); }
        }

        isSpawningGhost = false;
    }

    // 追跡/徘徊の開始・停止
    void StartFollow() { StopFollow(); followCo = StartCoroutine(FollowGhost()); }
    void StopFollow() { if (followCo != null) { StopCoroutine(followCo); followCo = null; } }
    void StartWander() { StopWander(); wanderCo = StartCoroutine(WanderGhost()); }
    void StopWander() { if (wanderCo != null) { StopCoroutine(wanderCo); wanderCo = null; } }

    void SwitchToSubCamera()
    {
        if (MainCamera)
        {
            MainCamera.enabled = false;
            var ml = MainCamera.GetComponent<AudioListener>();
            if (ml) ml.enabled = false;
        }
        if (SubCamera)
        {
            SubCamera.enabled = true;
            var sl = SubCamera.GetComponent<AudioListener>();
            if (sl) sl.enabled = true;
        }
    }
    void SwitchToMainCamera()
    {
        if (MainCamera)
        {
            MainCamera.enabled = true;
            var ml = MainCamera.GetComponent<AudioListener>();
            if (ml) ml.enabled = true;
        }
        if (SubCamera)
        {
            SubCamera.enabled = false;
            var sl = SubCamera.GetComponent<AudioListener>();
            if (sl) sl.enabled = false;
        }
    }

    // --------------------------------------------------------------------
    // ここから【怒りエフェクト制御】
    void EnsureAngryFxAttached()
    {
        if (angryFx || !AngryEffectPrefab || !currentghost) return;

        // 「Head」等のアンカーがあれば使う（任意）
        Transform anchor = currentghost.transform.Find("Head");
        Vector3 spawnPos = (anchor ? anchor.position : currentghost.transform.TransformPoint(AngryEffectLocalOffset));

        var go = Instantiate(AngryEffectPrefab, spawnPos, currentghost.transform.rotation);
        angryFx = go.transform;
        angryFxPs = go.GetComponent<ParticleSystem>();

        if (ParentEffectToGhost)
        {
            angryFx.SetParent(anchor ? anchor : currentghost.transform, worldPositionStays: false);
            angryFx.localPosition = anchor ? Vector3.zero : AngryEffectLocalOffset;
            angryFx.localRotation = Quaternion.identity;
        }
    }

    void ShowAngryFx()
    {
        EnsureAngryFxAttached();
        if (!angryFx) return;

        angryFx.gameObject.SetActive(true);

        if (angryFxPs)
        {
            var emission = angryFxPs.emission;
            emission.enabled = true;
            angryFxPs.Clear();
            angryFxPs.Play();
        }
    }

    void HideAngryFx(bool destroy = false)
    {
        if (!angryFx) return;

        if (destroy)
        {
            Destroy(angryFx.gameObject);
            angryFx = null;
            angryFxPs = null;
            return;
        }

        if (ShowOnlyWhileChasing)
        {
            if (angryFxPs)
            {
                var emission = angryFxPs.emission;
                emission.enabled = false;
                angryFxPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            angryFx.gameObject.SetActive(false);
        }
    }
    // ここまで【追加：怒りエフェクト制御】
    // --------------------------------------------------------------------
}
