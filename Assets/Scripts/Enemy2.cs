using UnityEngine;

public class Enemy2 : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float _playerBaseSpeed = 5f; //�v���C���[�̈ړ����x
    [SerializeField] private float _lifetime = 10f;       //�G�l�~�[�̎���
    [SerializeField] private float _rotationSpeed = 180f;//�U��Ԃ�p�x
    [SerializeField] private float _enemySpeed = 1.3f;//�G�l�~�[�̃X�s�[�h�{��
    [SerializeField] private float _rayDistance = 1.5f; //�I�u�W�F�N�g�̌��m�͈�

    private Rigidbody _rb;
    private float _speed;//�G�l�~�[�̑����I�ȃX�s�[�h
    private float _timer;//�������J�E���g���邽�߂̂���
    private bool _isRotation = false;//��]�����ǂ���
    private int _turnDirection = 1;// -1=��, 1=�E
    private Quaternion _targetRot;// �����̊p�x

    private void Start()
    {
        _rb = GetComponent<Rigidbody>();

        _speed = _playerBaseSpeed * _enemySpeed;//���x�v�Z�@�v���C���[���x*�{��

        _timer = _lifetime;//������
    }

    private void Update()
    {
        _timer -= Time.deltaTime;//1�b���ƂɃJ�E���g�����炵�āA0�ɂȂ��������
        if (_timer <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void FixedUpdate()
    {
        if (_isRotation)//��]��
        {
            // ���񏈗�
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetRot, _rotationSpeed * Time.fixedDeltaTime);

            // �ڕW�p�x�ɂقړ��B���������I��
            if (Quaternion.Angle(transform.rotation, _targetRot) < 1f)
            {
                _isRotation = false;
            }

            return; // ���񒆂͑O�i���Ȃ�
        }

        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, _rayDistance))//�O���Ƀ��C�̐���
        {
            if (hit.collider.CompareTag("Furniture"))//�q�b�g�����R���C�_�[�̃^�O����v������
            {
                _turnDirection = Random.Range(0, 2) == 0 ? 1 : -1; //���E�����_���ɉ���ł���悤�� 1 = �E, -1 = ��

                // ���p�������v�Z���Ă���
                _targetRot = Quaternion.LookRotation(Vector3.Cross(transform.forward, Vector3.up).normalized * _turnDirection, Vector3.up);
            }
            _isRotation = true;//��𓮍�ɓ���
            return;//���񒆂͈ړ����Ȃ��悤��
        }
        _rb.MovePosition(_rb.position + transform.forward * _speed * Time.fixedDeltaTime);//�O�i��������
    }

    private void OnCollisionEnter(Collision collision)
    {
        Vector3 reflectDir = Vector3.Reflect(transform.forward, collision.contacts[0].normal);//���˂̃x�N�g���v�Z
        transform.rotation = Quaternion.LookRotation(reflectDir, Vector3.up);//���˕����ɉ�]
    }
}
