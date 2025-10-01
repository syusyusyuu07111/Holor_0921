using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
public class Item : MonoBehaviour

{
    public GameObject Book1;
    public GameObject Book2;
    public GameObject Book3;
    public GameObject Book4;
    public GameObject Book5;
    public Transform Player;
    public float CheckDistance = 1.5f;
    public InputSystem_Actions input;
    public TextMeshProUGUI text;

    [Header("Sprites")]
    public UnityEngine.UI.Image TargetImage;
    public Sprite Book1Sprite;
    public Sprite Book2Sprite;
    public Sprite Book3Sprite;
    public Sprite Book4Sprite;
    public Sprite Book5Sprite;

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
        TargetImage.enabled = false;
    }

    void Update()
    {
        //�e�{�ɋ߂Â��Ċm�F����ƃA�C�e���̏����m�F���邱�Ƃ��ł���
        float disanceBook1 = Vector3.Distance(Player.transform.position, Book1.transform.position);
        float disanceBook2 = Vector3.Distance(Player.transform.position, Book2.transform.position);
        float disanceBook3 = Vector3.Distance(Player.transform.position, Book3.transform.position);
        float disanceBook4 = Vector3.Distance(Player.transform.position, Book4.transform.position);
        float disanceBook5 = Vector3.Distance(Player.transform.position, Book5.transform.position);

        //�ǂꂩ����̃A�C�e���ɋ߂Â����Ƃ��Ƀe�L�X�g�\��--------------------------------------------------------------------
        if (disanceBook1 < CheckDistance || disanceBook2 < CheckDistance || disanceBook3 < CheckDistance || disanceBook4 < CheckDistance || disanceBook5 < CheckDistance)
        {
            text.gameObject.SetActive(true);
        }
        else

        {
            TargetImage.enabled = false;
            text.gameObject.SetActive(false);
        }
        //--------------------------------------------------------------------------------------------------------------------

        // ���ǉ��F���̃t���[���Łu�����ꂽ�u�ԁv����������i�������ςȂ��◣�����u�Ԃ��E��Ȃ��j
        bool pressed = input.Player.Interact.WasPerformedThisFrame();

        //book1�̃A�C�e���m�F-------------------------------------------------------------------------------------------------
        if (disanceBook1 < CheckDistance && pressed)
        {
            // �������{�����ɕ\�����Ȃ��\���A����ȊO�͂��̖{��\���i�g�O������j
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
        //book2�̃A�C�e���m�F-------------------------------------------------------------------------------------------------
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
        //book3�̃A�C�e���m�F-------------------------------------------------------------------------------------------------
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
        //book4�̃A�C�e���m�F-------------------------------------------------------------------------------------------------
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
        //book5�̃A�C�e���m�F-------------------------------------------------------------------------------------------------
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
