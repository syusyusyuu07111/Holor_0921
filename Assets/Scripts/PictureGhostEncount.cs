using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class PictureGhostEncount : MonoBehaviour
{
    public Transform NorthPicture;
    public Transform EastPicture;
    public Transform WestPicture;
    public Transform Player;
    InputSystem_Actions input;
    public float TouchDistance=1.0f;
    public TextMeshProUGUI text;
    public GameObject Ghost;
    public GameObject DestroyWall;//あたりのえをひいたときに壊す壁
    public float GhostSpeed;

    private GameObject currentghost;//生成した幽霊を参照するよう
    public float GhostStopDistance = 0.2f; // 追加：近づきすぎたら止める距離（任意調整）

    public void Awake()
    {
        input = new InputSystem_Actions();
    }
    public void OnEnable()
    {
        input.Player.Enable();
    }
    private void Start()
    {
        if (text) text.gameObject.SetActive(false);
    }
    void Update()
    {
        //currentghost がいれば毎フレームプレイヤーに向かって移動（Transform追尾）
        if (currentghost != null && Player != null)
        {
            Vector3 to = Player.transform.position - currentghost.transform.position;
            to.y = 0f; // 上下を無視する場合（必要なければ消してOK

            float dist = to.magnitude;
            if (dist > GhostStopDistance) // 近すぎるなら止める
            {
                Vector3 dir = to.normalized;
                currentghost.transform.position += dir * Time.deltaTime * GhostSpeed;

                //向きをプレイヤー側へ
                if (dir.sqrMagnitude > 0.0001f)
                {
                    currentghost.transform.rotation = Quaternion.Slerp(
                        currentghost.transform.rotation,
                        Quaternion.LookRotation(dir, Vector3.up),
                        10f * Time.deltaTime
                    );
                }
            }
        }

        //絵の位置と距離取得--------------------------------------------------------------------------------------------------------------
        float NorthPictureDistance;
        NorthPictureDistance = Vector3.Distance(Player.transform.position, NorthPicture.transform.position);

        float EastPictureDistance;
        EastPictureDistance = Vector3.Distance(Player.transform.position, EastPicture.transform.position);

        float WestPictureDistance;
        WestPictureDistance = Vector3.Distance(Player.transform.position, WestPicture.transform.position);

        //どれかしらの絵に触れる距離にいるときの処理 テキスト表示
        if (NorthPictureDistance < TouchDistance || EastPictureDistance < TouchDistance || WestPictureDistance < TouchDistance)
        {
            if (text) text.gameObject.SetActive(true);
        }
        else
        {
            if (text) text.gameObject.SetActive(false);
        }

        //northpictureの絵と近いときの処理 当たりの絵--------------------------------------------------------------------------------------------
        if (NorthPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())//northpictureが触れる距離にあるときの挙動
        {
            Destroy(DestroyWall);
        }

        //Eastpictureの絵と近いときの処理　外れの絵---------------------------------------------------------------------------------------------
        if (EastPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())//eastpictureが触れる距離にあるときの挙動
        {
             if (currentghost == null)
            {
                currentghost = Instantiate(Ghost, EastPicture.transform.position, Quaternion.identity);
            }

        }

        //Westpictureの絵と近いときの処理　外れの絵--------------------------------------------------------------------------------------------
        if (WestPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())//Westpictureが触れる距離にあるときの挙動
        {
             if (currentghost == null)
            {
                currentghost = Instantiate(Ghost, WestPicture.transform.position, Quaternion.identity);

            }
        }
    }
}
