using UnityEngine;
using System.Collections;

/*
     このスクリプトがやること

     目的：
     ゴーストが「プレイヤーを見つけたら追跡する」処理をまとめたスクリプト。

     大きく分けて 4つ の仕事がある。

     1) トリガーで「見つけた」を検知する
        ・OnTriggerEnter で Player が入ったら「見つけた」扱い
        ・初回だけカメラ演出を入れる（cutOnlyOnce=true のとき）
        ・追跡をすぐ始めるか、演出が終わってから始めるか選べる（deferChaseUntilCutDone）

     2) 追跡中はゴーストをプレイヤーへ移動させる
        ・Update 中、isChasing=true の間だけ動く
        ・水平移動のみ（Yは無視）
        ・stopDistance より近い時は止めてめり込みを防ぐ
        ・向きをプレイヤー方向へ向けつつ前進する

     3) 追跡開始/終了で「怒りエフェクト」を出す
        ・追跡開始時に angryEffectPrefab を生成して表示する
        ・追跡終了時に消す（effectGraceSecondsOnStop で少し残すことも可能）
        ・parentEffectToGhost=true なら親子付け、false なら毎フレーム位置同期

     4) 追跡中に「詰まり」状態になったら解除する（Unstuck）
        ・一定時間ほとんど動いていない＝詰まり と判定する
        ・前方に障害物があれば手前へ押し出す
        ・それでもダメなら少し前へワープする
        ・最終手段として一瞬だけ Collider を Trigger 化して通り抜ける（usePhaseThrough=true）

     カメラ演出について（CutLookAtGhostAndZoomRoutine）：
     ・mainCamera は Transform を絶対に変更しない（enabled の ON/OFF だけ）
     ・lookCamera を一時的に有効化して、以下を行う
        ① 幽霊の方向へ向ける（lookToGhostSeconds）
        ② 幽霊へ寄る（zoomSeconds / stopDistanceFromGhost）
        ③ 少し保持（holdSeconds）
        ④ deferChaseUntilCutDone=true の場合、このタイミングで追跡を開始する
        ⑤ 追跡開始後も sub を postChaseReturnSeconds だけ維持する
        ⑥ mainCamera に戻す

     「追跡をやめる」条件について：
     ・chaseForeverAfterTriggered=true なら、OnTriggerExit では追跡を止めない
     ・false の時だけエリア外で追跡終了する
*/

public class PlayerChase : MonoBehaviour
{
    // ================== 参照 ==================
    [Header("参照")]
    public Transform Player;      // プレイヤー
    public Transform Ghost;       // ゴースト

    // ================== 移動設定 ==================
    [Header("移動設定")]
    public float moveSpeed = 3.0f;
    [Tooltip("停止する最小距離（めり込み防止に0.1〜0.3推奨）")]
    public float stopDistance = 0.2f;

    // ================== 追跡挙動 ==================
    [Header("挙動切替")]
    [Tooltip("一度発見したら、トリガー外に出ても追跡を続ける")]
    [SerializeField] private bool chaseForeverAfterTriggered = true;

    private bool isChasing = false;

    // ================== 見つけ演出・一回限り制御 ==================
    [Header("見つけ演出（カメラ切替）は一回だけにする")]
    [Tooltip("true: エリアに何度入ってもカメラ演出は最初の1回だけ")]
    [SerializeField] private bool cutOnlyOnce = true;
    private bool hasSpottedOnce = false; // 最初に見つけたか

    // ================== Unstuck（詰まり解除） ==================
    [Header("Unstuck（詰まり解除）")]
    [Tooltip("扉・壁など“引っかかる障害物”のレイヤー")]
    [SerializeField] private LayerMask environmentMask;
    [Tooltip("これ以下の速度が続いたら“詰まり”とみなす")]
    [SerializeField] private float stuckSpeedThreshold = 0.05f;
    [Tooltip("詰まり継続と判定する時間")]
    [SerializeField] private float stuckCheckSeconds = 0.35f;
    [Tooltip("まず前方に押し出す距離")]
    [SerializeField] private float pushStep = 0.6f;
    [Tooltip("それでも無理なら前に小さくワープする距離")]
    [SerializeField] private float warpAhead = 0.8f;
    [Tooltip("衝突面からどれだけ離して置くか")]
    [SerializeField] private float clearance = 0.15f;
    [Tooltip("最終手段：一瞬だけTrigger化して通り抜ける")]
    [SerializeField] private bool usePhaseThrough = true;
    [Tooltip("Trigger化の秒数")]
    [SerializeField] private float phaseDuration = 0.25f;

    // Unstuck内部
    private Vector3 _lastPos;
    private float _stuckTimer;
    private bool _isPhasing;
    private Collider _ghostCol;   // 本体の BoxCollider 等
    private Rigidbody _rb;        // 付いていれば利用

    // ================== 怒りエフェクト ==================
    [Header("怒りエフェクト")]
    [Tooltip("追跡中に表示するエフェクトのPrefab（ParticleSystem等）")]
    [SerializeField] private GameObject angryEffectPrefab;
    [Tooltip("ゴーストのローカル座標でのオフセット（頭上に出したい時は y を上げる）")]
    [SerializeField] private Vector3 effectLocalOffset = new Vector3(0f, 1.5f, 0f);
    [Tooltip("true=親子付け / false=Updateで位置同期")]
    [SerializeField] private bool parentEffectToGhost = false;
    [Tooltip("追跡終了後に残す時間（秒）。0なら即消し")]
    [SerializeField] private float effectGraceSecondsOnStop = 0f;

    private Transform angryEffectInstance;   // 生成したエフェクトのTransform
    private float effectStopTimer = 0f;

    // ================== カメラ切り替え演出 ==================
    [Header("カメラ切り替え演出")]
    [Tooltip("普段のメインカメラ（Transformは絶対に変更しない）")]
    [SerializeField] private Camera mainCamera;
    [Tooltip("幽霊へ向ける＆寄るためのサブカメラ（このカメラだけ動かす）")]
    [SerializeField] private Camera lookCamera;
    [Tooltip("まず幽霊へ向ける時間（秒）")]
    [SerializeField] private float lookToGhostSeconds = 0.25f;
    [Tooltip("向いたあと“ぐーっと寄る”時間（秒）")]
    [SerializeField] private float zoomSeconds = 0.35f;
    [Tooltip("どこまで寄るか（ゴーストからの距離）")]
    [SerializeField] private float stopDistanceFromGhost = 1.6f;
    [Tooltip("寄り切ったあと見せる保持時間（秒）")]
    [SerializeField] private float holdSeconds = 0.15f;

    [Tooltip("演出が終わるまで追跡開始を遅延する（今回：true運用）")]
    [SerializeField] private bool deferChaseUntilCutDone = true;

    [Tooltip("同一演出の多重起動ガード")]
    [SerializeField] private bool cutRunning = false;

    [Header("追跡開始後にサブを保つ時間")]
    [Tooltip("ズーム完了→追跡開始→この時間だけサブ継続→メイン復帰")]
    [SerializeField] private float postChaseReturnSeconds = 0.5f; // ★ここが今回の肝

    // ================== ライフサイクル ==================
    private void Awake()
    {
        if (!mainCamera && Camera.main) mainCamera = Camera.main;
        if (lookCamera) lookCamera.enabled = false; // 初期は無効

        _ghostCol = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();
        _rb = GetComponent<Rigidbody>();
        _lastPos = transform.position;
    }

    // ================== トリガー検知 ==================
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // すでに一度スポット済みなら、演出スキップで追跡のみ
        if (cutOnlyOnce && hasSpottedOnce)
        {
            isChasing = true;
            SpawnAngryEffect();
            return;
        }

        // 初見
        hasSpottedOnce = true;

        // 今回は「演出が終わるまで追跡を開始しない」
        if (deferChaseUntilCutDone)
        {
            StartCutLookAtGhostAndZoom(); // 追跡開始はコルーチン内の「ズーム完了後」
        }
        else
        {
            // 使わない前提だが互換維持
            isChasing = true;
            SpawnAngryEffect();
            StartCutLookAtGhostAndZoom();
        }
    }

    // ★エリア外に出ても追いかけ続ける仕様
    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (chaseForeverAfterTriggered) return; // ここで終了させない

        isChasing = false;
        BeginStopAngryEffect();
    }

    // ================== メインループ ==================
    private void Update()
    {
        //================
        // 追従移動（追跡中だけ）
        //================
        if (isChasing && Player && Ghost)
        {
            Vector3 toPlayer = Player.position - Ghost.position;
            toPlayer.y = 0f; // 水平のみ

            float dist = toPlayer.magnitude;
            if (dist > stopDistance)
            {
                if (toPlayer.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toPlayer);
                    Ghost.rotation = Quaternion.Slerp(Ghost.rotation, targetRot, 0.1f);
                }

                Vector3 next = Ghost.position + Ghost.forward * moveSpeed * Time.deltaTime;
                if (_rb && !_rb.isKinematic) _rb.MovePosition(next);
                else Ghost.position = next;
            }
        }

        //================
        // 怒りエフェクト追従＆停止処理
        //================
        if (angryEffectInstance)
        {
            if (!parentEffectToGhost && Ghost)
            {
                angryEffectInstance.position = Ghost.TransformPoint(effectLocalOffset);
                angryEffectInstance.rotation = Ghost.rotation;
            }

            if (!isChasing)
            {
                if (effectGraceSecondsOnStop > 0f)
                {
                    effectStopTimer -= Time.deltaTime;
                    if (effectStopTimer <= 0f)
                        DestroyAngryEffect();
                }
                else
                {
                    DestroyAngryEffect();
                }
            }
        }

        //================
        // 詰まり検知＆解除
        //================
        UnstuckTick(Time.deltaTime);
    }

    // ================== 怒りエフェクト ==================
    /*
         SpawnAngryEffect
         ・追跡開始時に呼ぶ
         ・Prefabが設定されていれば生成して表示する
         ・既に存在する場合は Particle をリスタートする
    */
    private void SpawnAngryEffect()
    {
        if (!angryEffectPrefab || !Ghost) return;

        if (!angryEffectInstance)
        {
            GameObject go = Instantiate(angryEffectPrefab);
            angryEffectInstance = go.transform;

            if (parentEffectToGhost)
            {
                angryEffectInstance.SetParent(Ghost, worldPositionStays: false);
                angryEffectInstance.localPosition = effectLocalOffset;
                angryEffectInstance.localRotation = Quaternion.identity;
            }
            else
            {
                angryEffectInstance.position = Ghost.TransformPoint(effectLocalOffset);
                angryEffectInstance.rotation = Ghost.rotation;
            }

            var ps = angryEffectInstance.GetComponent<ParticleSystem>();
            if (ps) ps.Play();
        }
        else
        {
            var ps = angryEffectInstance.GetComponent<ParticleSystem>();
            if (ps) { ps.Clear(); ps.Play(); }
        }

        effectStopTimer = effectGraceSecondsOnStop;
    }

    /*
         BeginStopAngryEffect
         ・追跡終了時に呼ぶ
         ・effectGraceSecondsOnStop が 0 なら即消し
         ・>0 ならタイマーで遅延消し
    */
    private void BeginStopAngryEffect()
    {
        if (angryEffectInstance)
        {
            effectStopTimer = effectGraceSecondsOnStop;
            if (effectGraceSecondsOnStop <= 0f) DestroyAngryEffect();
        }
    }

    private void DestroyAngryEffect()
    {
        if (!angryEffectInstance) return;
        Destroy(angryEffectInstance.gameObject);
        angryEffectInstance = null;
    }

    // ================== カメラ演出 ==================
    /*
         StartCutLookAtGhostAndZoom
         ・演出の入口
         ・必要な参照が揃っていて、演出が走っていなければ開始する
    */
    private void StartCutLookAtGhostAndZoom()
    {
        if (cutRunning || !lookCamera || !mainCamera || !Ghost) return;
        StartCoroutine(CutLookAtGhostAndZoomRoutine());
    }

    /*
         CutLookAtGhostAndZoomRoutine

         ① mainCamera OFF / lookCamera ON
         ② lookCamera を幽霊方向へ向ける
         ③ lookCamera を幽霊に寄せる
         ④ holdSeconds だけ保持
         ⑤ deferChaseUntilCutDone=true の時、ここで追跡を開始する
         ⑥ 追跡開始後も postChaseReturnSeconds だけサブで見せる
         ⑦ lookCamera OFF / mainCamera ON に戻す

         ※ mainCamera の Transform は触らない（位置を記録するだけ）
    */
    private IEnumerator CutLookAtGhostAndZoomRoutine()
    {
        cutRunning = true;

        // メインのTransformは触らない（記録だけ）
        Vector3 mainPos = mainCamera.transform.position;
        Quaternion mainRot = mainCamera.transform.rotation;

        // メイン無効→サブ有効（同フレーム切替）
        mainCamera.enabled = false;
        lookCamera.enabled = true;

        // ① 幽霊の方向へ向ける
        Quaternion fromRot = lookCamera.transform.rotation;
        Vector3 dir = Ghost.position - lookCamera.transform.position;
        if (dir.sqrMagnitude < 0.0001f) dir = lookCamera.transform.forward;
        Quaternion toRot = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float t = 0f;
        while (t < lookToGhostSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(lookToGhostSeconds, 0.0001f));
            k = 1f - Mathf.Pow(1f - k, 3f); // 立ち上がり速め
            lookCamera.transform.rotation = Quaternion.Slerp(fromRot, toRot, k);
            yield return null;
        }
        lookCamera.transform.rotation = toRot;

        // ② 幽霊へぐっと寄る
        Vector3 startPos = lookCamera.transform.position;
        Vector3 toGhost = (Ghost.position - startPos);
        Vector3 targetPos = Ghost.position - toGhost.normalized * Mathf.Max(0.1f, stopDistanceFromGhost);
        targetPos.y = startPos.y; // 高さは維持

        t = 0f;
        while (t < zoomSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(zoomSeconds, 0.0001f));
            k = k * k * (3f - 2f * k); // smoothstep
            lookCamera.transform.position = Vector3.Lerp(startPos, targetPos, k);

            Vector3 liveDir = (Ghost.position - lookCamera.transform.position).normalized;
            lookCamera.transform.rotation = Quaternion.LookRotation(liveDir, Vector3.up);
            yield return null;
        }
        lookCamera.transform.position = targetPos;

        // ③ 保持（静止見せ。不要なら0でスキップ）
        if (holdSeconds > 0f) yield return new WaitForSeconds(holdSeconds);

        // ④ ★ここで追跡開始★
        if (deferChaseUntilCutDone)
        {
            isChasing = true;
            SpawnAngryEffect();
        }

        // ⑤ サブを維持して“追跡開始直後の怖さ”を見せる
        float wait = Mathf.Max(0f, postChaseReturnSeconds);
        if (wait > 0f) yield return new WaitForSeconds(wait);

        // ⑥ メイン復帰
        lookCamera.enabled = false;
        mainCamera.enabled = true;

        cutRunning = false;
    }

    // ================== Unstuck 実装 ==================
    /*
         UnstuckTick
         ・追跡中だけ判定する
         ・移動速度が一定以下の状態が stuckCheckSeconds 続いたら TryUnstuck を実行
    */
    private void UnstuckTick(float dt)
    {
        if (!isChasing) { _stuckTimer = 0f; _lastPos = transform.position; return; }

        float speed = (transform.position - _lastPos).magnitude / Mathf.Max(dt, 0.0001f);
        bool notCloseEnough = (Player && Vector3.Distance(Ghost.position, Player.position) > stopDistance + 0.05f);

        if (notCloseEnough && speed < stuckSpeedThreshold)
            _stuckTimer += dt;
        else
            _stuckTimer = 0f;

        if (_stuckTimer >= stuckCheckSeconds)
        {
            TryUnstuck();
            _stuckTimer = 0f;
        }

        _lastPos = transform.position;
    }

    /*
         TryUnstuck
         ・前方に障害物があるなら「手前に押し出す」→ダメなら「面の向こうへ軽ワープ」
         ・前方に何も無いなら「少し前へワープ」
         ・最終手段：一定秒だけ Trigger 化して通り抜ける
    */
    private void TryUnstuck()
    {
        if (!Ghost) return;

        Vector3 fwd = Ghost.forward.normalized;
        Vector3 origin = Ghost.position + Vector3.up * 0.2f; // 低い位置から判定

        // 目前に障害物があるか
        if (Physics.Raycast(origin, fwd, out RaycastHit hit, pushStep, environmentMask, QueryTriggerInteraction.Ignore))
        {
            // ① 押し出し：面の手前まで寄せる
            Vector3 target = hit.point - fwd * Mathf.Max(0.01f, clearance);
            MoveTo(target);

            // まだ密着しているなら ② 面の向こう側へ軽ワープ
            if (Physics.Raycast(origin, fwd, out RaycastHit hit2, clearance * 1.2f, environmentMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 through = hit2.point + fwd * (clearance * 1.2f);
                MoveTo(through);
            }
        }
        else
        {
            // 目前にヒットなし → ③ 少し先へワープ
            Vector3 warp = Ghost.position + fwd * warpAhead;
            MoveTo(warp);
        }

        // ④ 最終手段：一瞬だけ Trigger 化して通過
        if (usePhaseThrough && !_isPhasing)
            StartCoroutine(PhaseThroughCoroutine());
    }

    private void MoveTo(Vector3 targetPos)
    {
        if (_rb && !_rb.isKinematic) _rb.MovePosition(targetPos);
        else Ghost.position = targetPos;
    }

    /*
         PhaseThroughCoroutine
         ・_ghostCol を一定時間だけ Trigger にする
         ・壁に引っかかって止まる最悪ケースの保険
    */
    private IEnumerator PhaseThroughCoroutine()
    {
        if (!_ghostCol) yield break;
        _isPhasing = true;

        bool originalTrigger = _ghostCol.isTrigger;
        _ghostCol.isTrigger = true;
        yield return new WaitForSeconds(phaseDuration);
        _ghostCol.isTrigger = originalTrigger;

        _isPhasing = false;
    }

    // ================== 便利メソッド（任意） ==================
    // 外部から強制的に追跡開始させたい時用
    public void ForceStartChase()
    {
        isChasing = true;
        SpawnAngryEffect();
    }

    // 外部から強制的に追跡停止させたい時用
    public void ForceStopChase()
    {
        isChasing = false;
        BeginStopAngryEffect();
    }
}