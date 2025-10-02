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

    public float GhostSpeed = 2.0f;          // 幽霊の移動速度
    public float GhostStopDistance = 0.2f;   // プレイヤーに近づきすぎたら停止する距離
    public float GhostLifetime = 10f;        // 幽霊の寿命（出現からこの秒数で消える）

    // カメラ（Display切替は使わず、enabled の切替のみ）
    public Camera MainCamera;                // 通常時に使うカメラ
    public Camera SubCamera;                 // 隠れている間に使うカメラ

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

    // ★追加: 二重生成防止フラグ
    private bool isSpawningGhost = false;

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

                // 入るときは WasPressedThisFrame() に
                if (input.Player.Interact.WasPressedThisFrame() && !Hide)
                {
                    // 隠れる前の位置/回転を保存（最初の一回だけ）
                    if (!savedPlayerValid)
                    {
                        savedPlayerPos = Player.transform.position;
                        savedPlayerRot = Player.transform.rotation;
                        savedPlayerValid = true;
                    }

                    if (HidePosition) Player.transform.position = HidePosition.position;
                    Hide = true;

                    // 隠れている間もガイダンステキストは出し続ける
                    if (text && !text.gameObject.activeSelf) text.gameObject.SetActive(true);

                    // サブカメラへ切替
                    SwitchToSubCamera();

                    // この瞬間から少しの間は解除キーを無視
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
            if (text && !text.gameObject.activeSelf) text.gameObject.SetActive(true);

            // 解除も WasPressedThisFrame() + クールダウン
            if (Time.time >= interactCooldownUntil && input.Player.Interact.WasPressedThisFrame())
            {
                ExitHide(true); // プレイヤー操作の解除 → 再召喚OK
                interactCooldownUntil = Time.time + 0.15f;
            }

            // 隠れ位置から左右(平面)へ一定距離動いたら自動解除 → 再召喚OK
            if (HidePosition && Player)
            {
                Vector3 p = Player.transform.position; p.y = 0f;
                Vector3 h = HidePosition.position; h.y = 0f;
                if (Vector3.Distance(p, h) > AutoExitDistance)
                {
                    ExitHide(true);
                    interactCooldownUntil = Time.time + 0.15f;
                }
            }
        }
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
        yield return new WaitForSeconds(AttackWaitTime);

        if (!Hide)
        {
            // ★修正: 生成は共通関数経由に統一
            SpawnGhostIfNeeded(true);
        }
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

        var anim = g.GetComponent<Animator>();
        if (anim != null && !string.IsNullOrEmpty(DisappearBoolName))
        {
            anim.SetBool(DisappearBoolName, true);
        }

        StartCoroutine(WaitDisappearAndDestroy(g, anim));
    }

    IEnumerator WaitDisappearAndDestroy(GameObject g, Animator anim)
    {
        float maxWait = 3f;

        if (anim != null && !string.IsNullOrEmpty(DisappearStateTag))
        {
            float t = 0f;
            while (t < maxWait)
            {
                var info = anim.GetCurrentAnimatorStateInfo(0);
                if (info.IsTag(DisappearStateTag))
                {
                    float waitLen = Mathf.Max(0.05f, info.length);
                    yield return new WaitForSeconds(waitLen);
                    break;
                }
                t += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(1.0f);
        }

        if (g != null) Destroy(g);
        currentghost = null;

        OnGhostEnd(); // ※再召喚しない
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
            OnGhostEnd(); // 再召喚しない
        }
    }

    void OnGhostEnd()
    {
        ExitHide(false); // 幽霊寿命で消えた → 再召喚しない
    }

    void ExitHide(bool respawn)
    {
        Hide = false;

        if (Player && savedPlayerValid)
        {
            Player.transform.SetPositionAndRotation(savedPlayerPos, savedPlayerRot);
            savedPlayerValid = false;
        }

        SwitchToMainCamera();

        // ★修正: 再出現要求があっても「重複生成防止」を厳密化
        if (respawn)
        {
            SpawnGhostIfNeeded(false);
        }
    }

    // ★追加: ゴースト生成を一元管理（同時生成/二重生成を防ぐ）
    void SpawnGhostIfNeeded(bool fromEncount)
    {
        if (currentghost != null) return;     // 既にいる
        if (isSpawningGhost) return;          // 生成中
        if (!Ghost || !Door) return;

        StartCoroutine(SpawnGhostRoutine(fromEncount));
    }

    IEnumerator SpawnGhostRoutine(bool fromEncount)
    {
        isSpawningGhost = true;               // 生成中フラグON
        yield return null;                    // 1フレーム待って衝突的な同時呼び出しを回避

        if (currentghost == null && Ghost && Door)
        {
            currentghost = Instantiate(Ghost, Door.transform.position, Quaternion.identity);

            if (audioSource && KnockVoice && !fromEncount)
            {
                audioSource.PlayOneShot(KnockVoice); // 解除での再出現時だけ鳴らす等、区別したいなら
            }

            StartCoroutine(GhostLifetimeRoutine());
            StartCoroutine(FollowGhost());
        }

        isSpawningGhost = false;              // 生成中フラグOFF
    }

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
}
