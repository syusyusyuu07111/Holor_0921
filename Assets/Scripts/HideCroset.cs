using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class HideCroset : MonoBehaviour
{
    public Transform Player;                             // �v���C���[�{��
    public List<Transform> CrosetLists = new List<Transform>();   // �N���[�[�b�g�Q�iTransform�j�����Ă���
    public bool hide = false;                             // �B��Ă��邩�ǂ���
    public InputSystem_Actions Input;                     // �VInputSystem�����N���X

    // --------------- �����p�iInspector����ύX�j ---------------
    public float OffsetForward = 0.30f;                   // �N���[�[�b�g�̉������i+�œ����ցj
    public float OffsetRight = 0.00f;                     // �E�����̔�����
    public float OffsetUp = 0.00f;                        // ������̔�����
    public float InteractRadius = 1.6f;                   // �ߐڔ���̔��a
    public MonoBehaviour[] MovementScriptsToDisable;      // �B��Ă���Ԃ����������������ړ��n�X�N���v�g

    // --------------- ������� ---------------
    private Transform _currentCloset;                      // �������Ă���N���[�[�b�g
    private Vector3 _cachedPos;                            // ����O�̈ʒu
    private Vector3 _lockedInsidePos;                      // �B��Ă���ԂɌŒ肷��ʒu
    private Collider[] _playerCols;                        // �v���C���[���R���C�_�[
    private readonly List<Collider> _closetCols = new List<Collider>(); // �N���[�[�b�g�̃R���C�_�[�Q

    private void Awake()
    {
        Input = new InputSystem_Actions();                 // ����
        if (!Player) Player = transform;                   // ���ݒ�Ȃ玩�g
        _playerCols = Player.GetComponentsInChildren<Collider>(true); // �Փ˖����p
    }

    private void OnEnable()
    {
        Input.Player.Enable();                             // �A�N�V�����L����
        // --------------- ���͍w�� ---------------
        Input.Player.Interact.performed += OnInterect;     // �Ԃ� Interect
    }

    private void OnDisable()
    {
        // --------------- ���͍w�ǉ��� ---------------
        Input.Player.Interact.performed -= OnInterect;     // ����
        Input.Player.Disable();
    }

    private void Update()
    {
        if (hide) Player.position = _lockedInsidePos;      // �B��Ă���Ԃ͈ʒu���Œ�itransform.position�̂݁j
    }

    // --------------- Interect���� ---------------
    private void OnInterect(InputAction.CallbackContext _)
    {
        if (hide) { ExitCloset(); return; }               // ���ɉB��Ă���Ώo��

        Transform closet = FindNearestCloset();           // �Ŋ��N���[�[�b�g����
        if (closet) EnterCloset(closet);                  // ������Γ���
    }

    // --------------- �Ŋ��N���[�[�b�g���� ---------------
    private Transform FindNearestCloset()
    {
        float best = float.MaxValue;                      // �ŒZ����
        Transform pick = null;                            // ���

        for (int i = 0; i < CrosetLists.Count; i++)
        {
            var t = CrosetLists[i];
            if (!t) continue;
            float d = (Player.position - GetClosetCenter(t)).sqrMagnitude; // ���S����
            if (d < best && d <= InteractRadius * InteractRadius) { best = d; pick = t; } // ���a���̂�
        }

        if (!pick)                                        // �t�H�[���o�b�N�i���X�g���ݒ�/�͂��Ȃ����j
        {
            Collider[] hits = Physics.OverlapSphere(Player.position, InteractRadius, ~0, QueryTriggerInteraction.Collide); // �SLayer
            foreach (var h in hits)
            {
                Transform t = h.transform;
                if (CrosetLists.Count > 0 && !CrosetLists.Contains(t)) continue; // ���X�g�^�p���̓��X�g�O�𖳎�
                float d = (Player.position - GetClosetCenter(t)).sqrMagnitude;
                if (d < best) { best = d; pick = t; }
            }
        }
        return pick;                                      // ������Ȃ����null
    }

    // --------------- ����iposition�̂݁j ---------------
    private void EnterCloset(Transform closet)
    {
        _currentCloset = closet;                          // ���݂̃N���[�[�b�g
        _cachedPos = Player.position;                     // �o��ʒu�����̈ʒu

        _closetCols.Clear();                              // �N���[�[�b�g��Collider���W
        closet.GetComponentsInChildren(true, _closetCols);
        ToggleIgnoreClosetCollision(true);                // �Փ˖���ON

        Vector3 center = GetClosetCenter(closet);         // �N���[�[�b�g���S
        Vector3 offset =
              (closet.forward * -OffsetForward)           // �������ցi+�œ����ɓ���j
            + (closet.right * OffsetRight)                // �E����������
            + (Vector3.up * OffsetUp);                    // �����������

        Vector3 targetPos = center + offset;              // �ڕW�ʒu
        Player.position = targetPos;                      // ���[�v�iposition�̂݁j
        _lockedInsidePos = targetPos;                     // ���b�N���W

        SetMovementEnabled(false);                        // �ړ��n�X�N���v�g�𖳌���
        hide = true;                                      // �B����ON
    }

    // --------------- �o��iposition�̂݁j ---------------
    private void ExitCloset()
    {
        Player.position = _cachedPos;                     // ����O�֖߂�
        ToggleIgnoreClosetCollision(false);               // �Փ˖���OFF
        _closetCols.Clear();

        SetMovementEnabled(true);                         // �ړ��n�X�N���v�g���ėL����
        _currentCloset = null;                            // �N���A
        hide = false;                                     // �B����OFF
    }

    // --------------- �N���[�[�b�g���S�擾 ---------------
    private Vector3 GetClosetCenter(Transform closet)
    {
        if (closet && closet.TryGetComponent<Collider>(out var col)) return col.bounds.center; // Collider�D��
        return closet ? closet.position : Player.position;                                      // �t�H�[���o�b�N
    }

    // --------------- �Փ˖����I��/�I�t ---------------
    private void ToggleIgnoreClosetCollision(bool ignore)
    {
        if (_playerCols == null || _playerCols.Length == 0) return; // �v���C���[��Collider��������Ή������Ȃ�
        for (int i = 0; i < _closetCols.Count; i++)
        {
            var c = _closetCols[i];
            if (!c) continue;
            for (int j = 0; j < _playerCols.Length; j++)
            {
                var pc = _playerCols[j];
                if (!pc) continue;
                Physics.IgnoreCollision(pc, c, ignore);   // �o���̏Փ˂𖳎�/����
            }
        }
    }

    // --------------- �ړ��X�N���v�g�̗L��/���� ---------------
    private void SetMovementEnabled(bool enabled)
    {
        if (MovementScriptsToDisable == null) return;     // Inspector���ݒ�Ȃ牽�����Ȃ�
        for (int i = 0; i < MovementScriptsToDisable.Length; i++)
        {
            var m = MovementScriptsToDisable[i];
            if (m) m.enabled = enabled;                   // �L��/������؂�ւ�
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Player) return;
        Gizmos.DrawWireSphere(Player.position, InteractRadius); // �͈͊m�F�p
    }
}
