using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

/*
     本アイテムの調査表示スクリプト
     ・プレイヤーが本に近づくと「調べる」UIを表示
     ・Interact押下で対応するSpriteを表示（トグル）
     ・同じ本をもう一度押すと非表示
*/

public class Item : MonoBehaviour
{
    //================
    // Inspector参照（本）
    //================

    public GameObject Book1;
    public GameObject Book2;
    public GameObject Book3;
    public GameObject Book4;
    public GameObject Book5;

    public Transform Player;
    public float CheckDistance = 1.5f;

    public InputSystem_Actions input;
    public TextMeshProUGUI text;

    //================
    // Sprite表示設定
    //================

    [Header("Sprites")]
    public UnityEngine.UI.Image TargetImage;
    public Sprite Book1Sprite;
    public Sprite Book2Sprite;
    public Sprite Book3Sprite;
    public Sprite Book4Sprite;
    public Sprite Book5Sprite;

    //================
    // Unity Lifecycle
    //================

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
        // 初期状態では画像は非表示
        TargetImage.enabled = false;
    }

    //================
    // Update
    //================

    void Update()
    {
        //================
        // 距離チェック（各本）
        //================

        // プレイヤーと各本との距離を測る
        float disanceBook1 = Vector3.Distance(Player.transform.position, Book1.transform.position);
        float disanceBook2 = Vector3.Distance(Player.transform.position, Book2.transform.position);
        float disanceBook3 = Vector3.Distance(Player.transform.position, Book3.transform.position);
        float disanceBook4 = Vector3.Distance(Player.transform.position, Book4.transform.position);
        float disanceBook5 = Vector3.Distance(Player.transform.position, Book5.transform.position);

        //================
        // 近づいたら「調べる」テキスト表示
        //================

        // どれか1冊でも範囲内ならテキストを表示
        if (disanceBook1 < CheckDistance || disanceBook2 < CheckDistance || disanceBook3 < CheckDistance || disanceBook4 < CheckDistance || disanceBook5 < CheckDistance)
        {
            text.gameObject.SetActive(true);
        }
        else
        {
            // 範囲外なら画像とテキストを非表示
            TargetImage.enabled = false;
            text.gameObject.SetActive(false);
        }

        //================
        // 入力（押された瞬間のみ判定）
        //================

        // 押しっぱなしではなく、このフレームで押された瞬間だけ取得
        bool pressed = input.Player.Interact.WasPerformedThisFrame();

        //================
        // Book1 調査処理（トグル）
        //================

        if (disanceBook1 < CheckDistance && pressed)
        {
            // 同じ本が既に表示中なら非表示、それ以外は表示する
            if (TargetImage.enabled && TargetImage.sprite == Book1Sprite)
            {
                TargetImage.enabled = false;
            }
            else
            {
                TargetImage.sprite = Book1Sprite;
                TargetImage.enabled = true;
            }
        }

        //================
        // Book2 調査処理（トグル）
        //================

        else if (disanceBook2 < CheckDistance && pressed)
        {
            if (TargetImage.enabled && TargetImage.sprite == Book2Sprite)
            {
                TargetImage.enabled = false;
            }
            else
            {
                TargetImage.sprite = Book2Sprite;
                TargetImage.enabled = true;
            }
        }

        //================
        // Book3 調査処理（トグル）
        //================

        else if (disanceBook3 < CheckDistance && pressed)
        {
            if (TargetImage.enabled && TargetImage.sprite == Book3Sprite)
            {
                TargetImage.enabled = false;
            }
            else
            {
                TargetImage.sprite = Book3Sprite;
                TargetImage.enabled = true;
            }
        }

        //================
        // Book4 調査処理（トグル）
        //================

        else if (disanceBook4 < CheckDistance && pressed)
        {
            if (TargetImage.enabled && TargetImage.sprite == Book4Sprite)
            {
                TargetImage.enabled = false;
            }
            else
            {
                TargetImage.sprite = Book4Sprite;
                TargetImage.enabled = true;
            }
        }

        //================
        // Book5 調査処理（トグル）
        //================

        else if (disanceBook5 < CheckDistance && pressed)
        {
            if (TargetImage.enabled && TargetImage.sprite == Book5Sprite)
            {
                TargetImage.enabled = false;
            }
            else
            {
                TargetImage.sprite = Book5Sprite;
                TargetImage.enabled = true;
            }
        }
    }
}