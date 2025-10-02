using System.Collections;
using TMPro;
using UnityEngine;

// キャラクターが３つ目の部屋に入った時に幽霊が出てくるから急いで隠れるギミックです============================================================================
public class HideArie : MonoBehaviour
{
    public AudioSource audioSource = null;
    public AudioClip KnockSe;

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

    private GameObject currentghost;         // 生成した幽霊の参照
    public float GhostLifetime = 10f;        // 幽霊の寿命

    // ★追加: カメラ参照（Display切替は使わず、enabledの切替のみ）
    public Camera MainCamera;                // 通常時に使うカメラ
    public Camera SubCamera;                 // 隠れている間に使うカメラ

    // 一度起動したら二度と起動しないためのフラグ
    private bool started = false;

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

        // 起動時のカメラ状態（同じDisplay1上で、enabledのON/OFFのみで切替）
        if (MainCamera) { MainCamera.enabled = true; }
        if (SubCamera) { SubCamera.enabled = false; }

        // AudioListenerの二重有効化防止
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

                // 隠れるボタンを押したら隠れる
                if (input.Player.Interact.WasPerformedThisFrame())
                {
                    if (HidePosition) Player.transform.position = HidePosition.position;
                    Hide = true;
                    if (text) text.gameObject.SetActive(false);

                    // 隠れた瞬間にサブカメラへ切替
                    SwitchToSubCamera();
                }
            }
            else
            {
                if (text) text.gameObject.SetActive(false);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Player")
        {
            // 一度だけ起動 以降はエリアを出入りしても再起動しない
            if (!started)
            {
                started = true;

                // カウント開始（この後はエリアを出ても止まらない）
                StartCoroutine(Encount());

                if (audioSource && KnockSe) audioSource.PlayOneShot(KnockSe);

                // 再トリガー防止のため、このコライダーを無効化
                var col = GetComponent<Collider>();
                if (col) col.enabled = false;
            }
        }
    }

    IEnumerator Encount()
    {
        // 侵入後、指定秒待ってから判定
        yield return new WaitForSeconds(AttackWaitTime);

        // 隠れていないなら襲ってくる
        if (!Hide)
        {
            if (Ghost && Door)
            {
                currentghost = Instantiate(Ghost, Door.transform.position, Quaternion.identity);
                Destroy(currentghost, GhostLifetime); // 10秒後に自動で消える
                StartCoroutine(FollowGhost());        // プレイヤーを追尾
            }
        }
        else
        {
            // 隠れているときは何もしない　あとで書く
        }
    }

    // 幽霊がTransformでプレイヤーを追いかける
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
    }

    // カメラ切替（Displayは固定。enabled のON/OFFだけ切替）
    void SwitchToSubCamera()
    {
        if (MainCamera) { MainCamera.enabled = false; var ml = MainCamera.GetComponent<AudioListener>(); if (ml) ml.enabled = false; }
        if (SubCamera) { SubCamera.enabled = true; var sl = SubCamera.GetComponent<AudioListener>(); if (sl) sl.enabled = true; }
    }
    void SwitchToMainCamera()
    {
        if (MainCamera) { MainCamera.enabled = true; var ml = MainCamera.GetComponent<AudioListener>(); if (ml) ml.enabled = true; }
        if (SubCamera) { SubCamera.enabled = false; var sl = SubCamera.GetComponent<AudioListener>(); if (sl) sl.enabled = false; }
    }
}
