using UnityEngine;

public class PeekCamera : MonoBehaviour
{
    public Transform Camera;
    public Transform Player;
    public Transform[] MovePositions;
    public TPSCamera tps;

    [SerializeField] OpenText openText;    // �� �����ɂ��̏ꏊ�� OpenText ��������
    public bool IsPeeking { get; private set; }

    InputSystem_Actions input;
    Vector3 savepos;

    void Awake() { input = new InputSystem_Actions(); }
    void OnEnable() { input.Player.Enable(); }

    void Update()
    {
        // �߂�pivot���擾
        Transform nearest = Nearest(Player.position, MovePositions);

        // �������|�C���g�FOpenText.instance ���g�킸�A������ openText ������
        bool canOpenHere = openText != null && openText.CanOpen;

        if (!IsPeeking && canOpenHere && input.Player.Interact.triggered)
        {
            savepos = Camera.position;
            tps.ControlEnable = false;
            if (nearest) Camera.position = nearest.position;
            IsPeeking = true;
        }
        else if (IsPeeking && input.Player.Interact.triggered)
        {
            IsPeeking = false;
            Camera.position = savepos;
            tps.ControlEnable = true;
        }
    }

    Transform Nearest(Vector3 fromPos, Transform[] pivots)
    {
        float best = float.PositiveInfinity;
        Transform bestpos = null;
        foreach (var p in pivots)
        {
            if (!p) continue;
            float d = (p.position - fromPos).sqrMagnitude;
            if (d < best) { best = d; bestpos = p; }
        }
        return bestpos;
    }
}
