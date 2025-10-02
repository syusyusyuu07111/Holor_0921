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
    public GameObject DestroyWall;//������̂����Ђ����Ƃ��ɉ󂷕�
    public float GhostSpeed;

    private GameObject currentghost;//���������H����Q�Ƃ���悤
    public float GhostStopDistance = 0.2f; // �ǉ��F�߂Â���������~�߂鋗���i�C�Ӓ����j

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
        //currentghost ������Ζ��t���[���v���C���[�Ɍ������Ĉړ��iTransform�ǔ��j
        if (currentghost != null && Player != null)
        {
            Vector3 to = Player.transform.position - currentghost.transform.position;
            to.y = 0f; // �㉺�𖳎�����ꍇ�i�K�v�Ȃ���Ώ�����OK

            float dist = to.magnitude;
            if (dist > GhostStopDistance) // �߂�����Ȃ�~�߂�
            {
                Vector3 dir = to.normalized;
                currentghost.transform.position += dir * Time.deltaTime * GhostSpeed;

                //�������v���C���[����
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

        //�G�̈ʒu�Ƌ����擾--------------------------------------------------------------------------------------------------------------
        float NorthPictureDistance;
        NorthPictureDistance = Vector3.Distance(Player.transform.position, NorthPicture.transform.position);

        float EastPictureDistance;
        EastPictureDistance = Vector3.Distance(Player.transform.position, EastPicture.transform.position);

        float WestPictureDistance;
        WestPictureDistance = Vector3.Distance(Player.transform.position, WestPicture.transform.position);

        //�ǂꂩ����̊G�ɐG��鋗���ɂ���Ƃ��̏��� �e�L�X�g�\��
        if (NorthPictureDistance < TouchDistance || EastPictureDistance < TouchDistance || WestPictureDistance < TouchDistance)
        {
            if (text) text.gameObject.SetActive(true);
        }
        else
        {
            if (text) text.gameObject.SetActive(false);
        }

        //northpicture�̊G�Ƌ߂��Ƃ��̏��� ������̊G--------------------------------------------------------------------------------------------
        if (NorthPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())//northpicture���G��鋗���ɂ���Ƃ��̋���
        {
            Destroy(DestroyWall);
        }

        //Eastpicture�̊G�Ƌ߂��Ƃ��̏����@�O��̊G---------------------------------------------------------------------------------------------
        if (EastPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())//eastpicture���G��鋗���ɂ���Ƃ��̋���
        {
             if (currentghost == null)
            {
                currentghost = Instantiate(Ghost, EastPicture.transform.position, Quaternion.identity);
            }

        }

        //Westpicture�̊G�Ƌ߂��Ƃ��̏����@�O��̊G--------------------------------------------------------------------------------------------
        if (WestPictureDistance < TouchDistance && input.Player.Interact.WasPerformedThisFrame())//Westpicture���G��鋗���ɂ���Ƃ��̋���
        {
             if (currentghost == null)
            {
                currentghost = Instantiate(Ghost, WestPicture.transform.position, Quaternion.identity);

            }
        }
    }
}
