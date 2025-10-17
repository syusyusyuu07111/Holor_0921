using UnityEngine;
using UnityEngine.InputSystem;

public class push : MonoBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private float _pushDistance = 0.2f;      // �Ƌ�����o���鋗��
    [SerializeField] private float _pushForce = 2f;         // �����́i�����߂ɂ��ăX���C�h���j
    [SerializeField] private LayerMask _LayerPositoin;      // ������I�u�W�F�N�g�̃��C���[

    [Header("References")]
    [SerializeField] private Transform _playerCamera;       // �v���C���[�̎��_�i�J�����j
    [SerializeField] private float _alignAngleLimit = 60f;  // ������p�x�͈̔�

    private Rigidbody _pushingRb = null;                    // �����Ă���I�u�W�F�N�g
    private Vector3 _pushDirection;                         // ��������

    private void Update()
    {
        // Ray�Ő��ʂ̃I�u�W�F�N�g���`�F�b�N
        Ray ray = new Ray(_playerCamera.position, _playerCamera.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin))
        {
            // E�L�[�����������Ă����
            if (Input.GetKey(KeyCode.F))
            {
                // �����Ă���I�u�W�F�N�g���擾
                if (_pushingRb == null)
                {
                    _pushingRb = hit.rigidbody;
                    if (_pushingRb != null)
                    {
                        // Ray�����������ʂ̖@���x�N�g���̋t�����։����i�����������j
                        _pushDirection = -hit.normal;
                        Debug.Log("�������H");
                    }
                }
            }
            else
            {
                // �L�[�𗣂��������
                _pushingRb = null;
            }
        }
        else
        {
            // �����q�b�g���Ă��Ȃ���Ή���
            _pushingRb = null;
        }
    }

    private void FixedUpdate()
    {
        // �����Ă���ԁA�����͂�������������
        if (_pushingRb != null)
        {
            _pushingRb.AddForce(_pushDirection * _pushForce, ForceMode.Force);
            Debug.Log("�͉�����Ă�H");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_playerCamera != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(_playerCamera.position, _playerCamera.forward * _pushDistance);
        }
    }

}

