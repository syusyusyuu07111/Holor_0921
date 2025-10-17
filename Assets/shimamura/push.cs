using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using static UnityEditor.Experimental.GraphView.GraphView;

public class push : MonoBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private float _pushDistance = 0.2f;      // �Ƌ�����o���鋗��
    [SerializeField] private float _pushForce = 2f;         // �����́i�����߂ɂ��ăX���C�h���j
    [SerializeField] private LayerMask _LayerPositoin;      // ������I�u�W�F�N�g�̃��C���[

    [Header("References")]
    [SerializeField] private Transform _rayOrigin;       �@�@�@// ���C���΂��N�_
    [SerializeField] private float _angleLimit = 45f;  // ������p�x�͈̔�
    [SerializeField] private Transform _player;                // �v���C���[�{�́i�ʒu�𓮂����Ώہj

    [Header("Jump Settings")]
    [SerializeField] private float _jumpDuration = 0.7f;        // ��Ԏ��ԁi�b�j
    [SerializeField] private float _jumpHeight = 0.75f;            // �������̍���

    [Header("Item Pickup Settings")]
    [SerializeField] private float _itemPickupRange = 1f;       // �A�C�e�����E���鋗��
    [SerializeField] private LayerMask _itemLayer;              // �A�C�e���̃��C���[�i��FItem�j

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI _itemTextMeshPro;
    [SerializeField] private TextMeshProUGUI _pushTextMeshPro;

    private Rigidbody _pushingRb = null;                    // �����Ă���I�u�W�F�N�g
    private Vector3 _pushDirection;                         // ��������

    //�W�����v�֘A
    private bool _isJumping = false;                    // ��s���t���O
    private Vector3 _jumpStart;                                  // �W�����v�J�n�ʒu
    private Vector3 _jumpEnd;                                    // �W�����v�ڕW�ʒu
    private float _jumpElapsed = 0f;                             // �W�����v�o�ߎ���
    private bool _isOnFurniture = false;                        // �Ƌ�̏�ɏ���Ă��邩

    //�C���x���g��
    private List<GameObject> _inventory = new List<GameObject>();  // �E�����A�C�e����ۑ����郊�X�g

    private void Update()
    {
        if (_isJumping)
        {
            
            UpdateJump();
            return;
        }

        if (_isOnFurniture)
        {
            CheckItemPickup();
        }


        // Ray�Ő��ʂ̃I�u�W�F�N�g���`�F�b�N
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin))
        {
            _pushTextMeshPro.SetText("E�L�[�ň֎q�ɏ��\nF�L�[�ň֎q������");
            ChairPush(hit);
            HandleJumpInput(hit);
        }
        else
        {
            // �����q�b�g���Ă��Ȃ���Ή���
            _pushingRb = null;

            _pushTextMeshPro.SetText("");
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
            _isOnFurniture = true; // �������ǉ�
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

    /// <summary>
    /// �Ƌ�̏�ŃA�C�e�����擾���鏈��
    /// </summary>
    private void CheckItemPickup()
    {
        Collider[] items = Physics.OverlapSphere(_player.position, _itemPickupRange, _itemLayer);

        // �Y������A�C�e����1���Ȃ��ꍇ�͏������X�L�b�v
        if (items.Length == 0)
        {
            _itemTextMeshPro.SetText("");
            return;
        }


        _itemTextMeshPro.SetText("E�L�[�A�C�e�������");

        // ��ԋ߂��A�C�e�����L�^����ϐ�
        Collider nearestItem = null;
        float minDist = Mathf.Infinity;

        // �擾�����S�A�C�e���𒲂ׂ�
        foreach (var item in items)
        {
            // ���C���[�����������Ċm�F�iOverLapSphere�̌��ʂɑ��̕������������ꍇ�p�j
            if (((1 << item.gameObject.layer) & _itemLayer) == 0) continue;

            // �������v�Z
            float dist = Vector3.Distance(_player.position, item.transform.position);

            // ���߂���΍X�V
            if (dist < minDist)
            {
                minDist = dist;
                nearestItem = item;
            }
        }

        //��ԋ߂��A�C�e��������AE�L�[����������E��
        if (nearestItem != null && Input.GetKeyDown(KeyCode.E))
        {
            _inventory.Add(nearestItem.gameObject);//�C���x���g���ǉ�
            Debug.Log($"�A�C�e���擾: {nearestItem.name}");
            Destroy(nearestItem.gameObject);
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

