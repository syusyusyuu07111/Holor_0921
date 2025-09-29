using UnityEngine;

public class PeekCamera : MonoBehaviour
{
    public static PeekCamera Instance;
    public Transform Camera;
    public Transform Player;
    public Transform[] MovePositions;
    private Transform Selectposition;
    InputSystem_Actions input;
    public TPSCamera tps;//�J��������
    public bool IsPeeking { get; set; }
    private Vector3 savepos;
    private void Awake()
    {
        Instance = this;
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }
    private void Start()
    {
        IsPeeking = false;

    }
    void Update()
    //�`���@�\�@�v���C���[�Ƌ߂�pivot�ɃJ��������点��===============================================================

    {
        //�v���C���[�Ƌ߂�pivot��T��---------------------------------------------------------------------------------------
        Transform nearest = Nearest(Player.transform.position, MovePositions);

        //�v���C���[�Ɉ�ԋ߂�pivot�ɃJ�������ړ�������---------------------------------------------------------------------
        if (IsPeeking == false && OpenText.instance.CanOpen && input.Player.Interact.triggered)
        {
            savepos = Camera.transform.position;           // ���ʒu��ۑ�
            tps.ControlEnable = false;                      // TPS��~
            if (nearest != null)
            {
                Camera.transform.position = nearest.transform.position;
            }
            IsPeeking = true;                               // �`����Ԃɂ���
        }
        //�J�����ʒu�߂�------------------------------------------------------------------------------------------------------
        else if (IsPeeking == true && input.Player.Interact.triggered)
        {
            IsPeeking = false;
            Camera.position = savepos;                      // �߂�
            tps.ControlEnable = true;                       // TPS�ĊJ
        }
    }
    //�Ŋ���pivot��Ԃ�
    Transform Nearest(Vector3 fromPos, Transform[] pivots)
    {
        float best = float.PositiveInfinity;//�����킩��Ȃ�����ő剻�����ď��������Ă���
        Transform bestpos = null;

        //�z��̒��̍��W���擾����----------------------------------------------------------------------------------------
        foreach (Transform transformpivot in pivots)
        {
            if (!transformpivot) continue;                 //
            Vector3 pos = transformpivot.position;
            float distance = (transformpivot.position - fromPos).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                bestpos = transformpivot;
            }
        }
        return bestpos;
        //------------------------------------------------------------------------------------------------------------------
    }
}
