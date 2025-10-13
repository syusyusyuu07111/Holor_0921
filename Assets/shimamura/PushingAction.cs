using UnityEngine;
using UnityEngine.InputSystem;

public class PushingAction: MonoBehaviour
{
    [SerializeField] private float _rayDistance = 1.5f;       // �Ƌ�����o���鋗��
    [SerializeField] private LayerMask _pushableLayer;        // ������Ƌ�̃��C���[
    [SerializeField] private Transform _holdPoint;            // �Ƌ����������ʒu�i�v���C���[�̑O���j
    [SerializeField] private float _psuhSpeed = 2f;
    private bool _isPushing = false;          // �����Ă����Ԃ��ǂ���
    private Transform _pushingObj;            // �����Ă���Ƌ�� Transform
    private Quaternion _lockedRotation;     // �����Ă���Ԃ̌Œ肳�ꂽ��]�p�x
    private Transform _tr;
    private PlayerInput _inputAction;

    private void Start()
    {
        _tr = GetComponent<Transform>();
        _inputAction = GetComponent<PlayerInput>();
    }

    void Update()
    {
        // push�L�[�̌��o
        if (_inputAction.actions["Push"].WasPressedThisFrame() && !_isPushing)
        {
            // �O���ɉƋ���邩Raycast�Ŋm�F
            if (Physics.Raycast(_tr.position, _tr.forward, out RaycastHit hit, _rayDistance, _pushableLayer))
            {
                // �Ƌ��transform���擾
                _pushingObj = hit.collider.transform;

                // �Ƌ���v���C���[�̎q�ɂ��� holdPoint �Ɉړ�
                _pushingObj.SetParent(_holdPoint);
                _pushingObj.localPosition = Vector3.zero;
                //_pushingObj.localRotation = Quaternion.identity; //��]�̃��Z�b�g

                //�v���C���[��]���Œ�
                _lockedRotation = _tr.rotation;

                _isPushing = true;
            }
        }

        // �L�[��b������
        if (_inputAction.actions["Push"].WasCompletedThisFrame() && _isPushing)
        {

            // �v���C���[�̎q�I�u�W�F�N�g����O��
            _pushingObj.SetParent(null);

            _isPushing = false;
            _pushingObj = null;
        }

        // �����Ă�Ԃ͑O�������͂̂݋���
        if (_isPushing)
        {
            // ��]�����b�N�i�����Ă�Ԃ͌�����ς��Ȃ��j
            _tr.rotation = _lockedRotation;
            if (_inputAction.actions["Push"].IsPressed())
            {
                _tr.position += _tr.forward * Time.deltaTime * _psuhSpeed;// �����Ă鎞�̃v���C���[�O�i�i�v�ύX�j
            }
        }
    }

    void OnDrawGizmosSelected()//�V�[���Ń��C������
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position,transform.forward * _rayDistance);
    }
}
