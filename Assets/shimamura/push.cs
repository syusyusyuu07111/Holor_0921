using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEditor.Experimental.GraphView.GraphView;

public class push : MonoBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private float _pushDistance = 0.2f;      // �Ƌ�����o���鋗��
    [SerializeField] private float _pushForce = 2f;         // �����́i�����߂ɂ��ăX���C�h���j
    [SerializeField] private LayerMask _LayerPositoin;      // ������I�u�W�F�N�g�̃��C���[

    [Header("References")]
    [SerializeField] private Transform _rayOrigin;       �@�@�@// ���C���΂��N�_
    [SerializeField] private float _angleLimit = 60f;  // ������p�x�͈̔�
    [SerializeField] private Transform _player;                // �v���C���[�{�́i�ʒu�𓮂����Ώہj

    [Header("Jump Settings")]
    [SerializeField] private float _jumpDuration = 0.7f;        // ��Ԏ��ԁi�b�j
    [SerializeField] private float _jumpHeight = 2f;            // �������̍���

    private Rigidbody _pushingRb = null;                    // �����Ă���I�u�W�F�N�g
    private Vector3 _pushDirection;                         // ��������

    //�W�����v�֘A
    private bool _isJumping = false;                    // ��s���t���O
    private Vector3 _jumpStart;                                  // �W�����v�J�n�ʒu
    private Vector3 _jumpEnd;                                    // �W�����v�ڕW�ʒu
    private float _jumpElapsed = 0f;                             // �W�����v�o�ߎ���


    private void Update()
    {
        if (_isJumping)
        {
            UpdateJump();
            return;
        }


        // Ray�Ő��ʂ̃I�u�W�F�N�g���`�F�b�N
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin))
        {
            ChairPush(hit);
            HandleJumpInput(hit);
        }
        else
        {
            // �����q�b�g���Ă��Ȃ���Ή���
            _pushingRb = null;
        }
    }

    /// <summary>
    /// F�L�[�ň֎q����������
    /// </summary>
    private void ChairPush(RaycastHit hit)
    {
        if (Input.GetKey(KeyCode.F))
        {
            if (_pushingRb == null)
            {
                _pushingRb = hit.rigidbody;
                if (_pushingRb != null)
                {
                    _pushDirection = -hit.normal; // �������������C�̋t�����ɐݒ�
                    Debug.Log("�����Ώ�: " + hit.transform.name);
                }
            }
        }
        else
        {
            _pushingRb = null; // F�L�[�𗣂��������
        }
    }

    /// <summary>
    /// �������W�����v�̍X�V����
    /// </summary>
    private void UpdateJump()
    {
        _jumpElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_jumpElapsed / _jumpDuration); // 0��1 �ɐ��K��

        // �����ړ��iLerp�j
        Vector3 horizontal = Vector3.Lerp(_jumpStart, _jumpEnd, t);

        // �����ړ��i�������j
        float height = Mathf.Sin(t * Mathf.PI) * _jumpHeight;

        _player.position = new Vector3(horizontal.x, horizontal.y + height, horizontal.z);

        if (t >= 1f)
        {
            _isJumping = false;
            Debug.Log("�Ƌ�̏�ɒ��n�����I");
        }
    }

    /// <summary>
    /// E�L�[�ň֎q�̏�ɕ������W�����v���鏈��
    /// </summary>
    private void HandleJumpInput(RaycastHit hit)
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            Collider col = hit.collider;
            if (col == null) return;

            // �I�u�W�F�N�g�̏�ʒ������W�����v�ڕW�ɐݒ�
            Vector3 topCenter = col.bounds.center + Vector3.up * col.bounds.extents.y;
            _jumpStart = _player.position;
            _jumpEnd = topCenter + Vector3.up * 0.05f;

            _jumpElapsed = 0f;
            _isJumping = true;

            Debug.Log("�������W�����v�J�n �� " + hit.transform.name);
        }
    }


    private void FixedUpdate()
    {
        // �����Ă���ԁA�����͂�������������
        if (_pushingRb != null)
        {
            _pushingRb.AddForce(_pushDirection * _pushForce, ForceMode.Force);//�p���I�ɗ͂�������
            Debug.Log("�͉�����Ă�H");
        }
    }

    private void OnDrawGizmosSelected() //�V�[����Ray������
    {
        if (_rayOrigin != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _pushDistance);
        }
    }



}

