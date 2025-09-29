using UnityEngine;

public class PeekCamera : MonoBehaviour
{
    public Transform Camera;
    public Transform Player;
    public Transform[] MovePositions;
    private Transform Selectposition;
    InputSystem_Actions input;

    private void Awake()
    {
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }

    void Update()
        //�`���@�\�@�v���C���[�Ƌ߂�pivot�ɃJ��������点��===============================================================

    {
        //�v���C���[�Ƌ߂�pivot��T��---------------------------------------------------------------------------------------
        Transform nearest = Nearest(Player.transform.position, MovePositions);

        Debug.Log($"DBG cam:{Camera} player:{Player} pivots:{MovePositions?.Length} nearest:{nearest} openText:{OpenText.instance}");

        //�v���C���[�Ɉ�ԋ߂�pivot�ɃJ�������ړ�������---------------------------------------------------------------------
        if (OpenText.instance.CanOpen&&input.Player.Interact.triggered)
        {
            Camera.transform.position = nearest.transform.position;
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
            Vector3 pos = transformpivot.position;
            float distance = (transformpivot.position - fromPos).sqrMagnitude;
            if(distance<best)
            {
                best = distance;
                bestpos = transformpivot;
            }
        }
        return bestpos;
        //------------------------------------------------------------------------------------------------------------------
    }
}
