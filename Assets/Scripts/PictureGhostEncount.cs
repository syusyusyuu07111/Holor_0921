using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using System.Collections; // ★追加：コルーチン用

/*
     このスクリプトがやること

     目的：
     3枚の絵（North / East / West）に対して、
     プレイヤーが近づいて Interact を押した時のイベントを分岐させる。

     全体の流れ：

     1) プレイヤーが絵に近い間だけ「調べる」系のテキストを表示する

     2) NorthPicture（当たり）
        ・演出コルーチンを開始する
        ・壁を映すカメラに切り替える
        ・PreDestroyDelay 秒待つ
        ・DestroyWall を Destroy する（壁を壊す）
        ・AfterDestroyDelay 秒待つ
        ・元のカメラに戻す

        ※ 多重起動防止のため、演出中は isShowingWall=true にして二重実行しない

     3) EastPicture / WestPicture（外れ）
        ・幽霊がまだ出ていない場合だけ、絵の位置に Ghost を生成する
        ・生成した幽霊は currentghost に保持する（1体だけにしたい）

     4) 幽霊が出ている間は毎フレーム追尾する
        ・プレイヤー方向へ GhostSpeed で移動する
        ・Y（上下）は無視して水平移動だけにする
        ・GhostStopDistance より近づいたら移動を止める
        ・移動中はプレイヤー方向へ向きを向ける（Slerpでなめらかに）

     カメラ周りの注意：
     ・MainCamera が未設定なら Camera.main を使う
     ・SafeSetCamActive で null を安全に処理してON/OFFする
*/

public class PictureGhostEncount : MonoBehaviour
{
    //================
    // References
    //================
    public Transform NorthPicture;                 // 当たりの絵
    public Transform EastPicture;                  // 外れの絵
    public Transform WestPicture;                  // 外れの絵
    public Transform Player;                       // プレイヤー

    InputSystem_Actions input;                     // 新InputSystem

    //================
    // Interaction
    //================
    public float TouchDistance = 1.0f;             // 絵を調べられる距離
    public TextMeshProUGUI text;                   // 「調べる」などのテキスト

    //================
    // Ghost
    //================
    public GameObject Ghost;                       // 生成する幽霊プレハブ
    public float GhostSpeed;                       // 幽霊の追跡速度
    public float GhostStopDistance = 0.2f;         // 近づきすぎたら止める距離

    private GameObject currentghost;               // 生成した幽霊（1体だけにするため保持）

    //================
    // Wall / Camera
    //================
    public GameObject DestroyWall;                 // 当たりの絵を引いたときに壊す壁

    public Camera MainCamera;                      // ふだん使うカメラ
    public Camera WallCamera;                      // 壁（床）を見る演出用カメラ

    public float PreDestroyDelay = 1.0f;           // カメラ切替 → 破壊までの待ち
    public float AfterDestroyDelay = 1.0f;         // 破壊後 → 戻すまでの待ち

    bool isShowingWall = false;                    // 演出の多重起動防止

    //================
    // Unity Lifecycle
    //================
    public void Awake()
    {
        input = new InputSystem_Actions();

        // 初期カメラ状態の安全設定
        if (MainCamera == null && Camera.main != null) MainCamera = Camera.main;
        SafeSetCamActive(MainCamera, true);
        SafeSetCamActive(WallCamera, false);
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
        //================
        // Ghost追尾（出現している間だけ）
        //================
        if (currentghost != null && Player != null)
        {
            Vector3 to = Player.transform.position - currentghost.transform.position;
            to.y = 0f; // 上下を無視して水平追尾

            float dist = to.magnitude;
            if (dist > GhostStopDistance)
            {
                Vector3 dir = to.normalized;

                // 移動
                currentghost.transform.position += dir * Time.deltaTime * GhostSpeed;

                // 向きをプレイヤー側へ
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

        //================
        // 絵との距離計算
        //================
        float NorthPictureDistance;
        NorthPictureDistance = Vector3.Distance(Player.transform.position, NorthPicture.transform.position);

        float EastPictureDistance;
        EastPictureDistance = Vector3.Distance(Player.transform.position, EastPicture.transform.position);

        float WestPictureDistance;
        WestPictureDistance = Vector3.Distance(Player.transform.position, WestPicture.transform.position);

        //================
        // 近い時だけテキスト表示
        //================
        if (NorthPictureDistance < TouchDistance || EastPictureDistance < TouchDistance || WestPictureDistance < TouchDistance)
        {
            if (text) text.gameObject.SetActive(true);
        }
        else
        {
            if (text) text.gameObject.SetActive(false);
        }

        //================
        // 当たり：NorthPicture
        //================
        if (NorthPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())
        {
            StartCoroutine(ShowWallThenDestroy());
        }

        //================
        // 外れ：EastPicture
        //================
        if (EastPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())
        {
            if (currentghost == null)
            {
                currentghost = Instantiate(Ghost, EastPicture.transform.position, Quaternion.identity);
            }
        }

        //================
        // 外れ：WestPicture
        //================
        if (WestPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())
        {
            if (currentghost == null)
            {
                currentghost = Instantiate(Ghost, WestPicture.transform.position, Quaternion.identity);
            }
        }
    }

    //================
    // Wall演出
    //================
    /*
         カメラ切替 → 待機 → 壁破壊 → 待機 → カメラ戻し

         ・isShowingWall で多重起動を防止する
         ・DestroyWall が null なら破壊はスキップする
    */
    IEnumerator ShowWallThenDestroy()
    {
        if (isShowingWall) yield break;
        isShowingWall = true;

        SwitchCamera(true);

        yield return new WaitForSeconds(PreDestroyDelay);

        if (DestroyWall != null)
        {
            Destroy(DestroyWall);
        }

        yield return new WaitForSeconds(AfterDestroyDelay);

        SwitchCamera(false);
        isShowingWall = false;
    }

    //================
    // Camera Switch
    //================
    /*
         toWall=true なら WallCamera をON、MainCameraをOFF
         toWall=false なら MainCamera をON、WallCameraをOFF
    */
    void SwitchCamera(bool toWall)
    {
        SafeSetCamActive(MainCamera, !toWall);
        SafeSetCamActive(WallCamera, toWall);
    }

    //================
    // Safe SetActive
    //================
    // null安全にカメラのGameObjectをON/OFFする
    void SafeSetCamActive(Camera cam, bool active)
    {
        if (cam == null) return;
        cam.gameObject.SetActive(active);
    }
}