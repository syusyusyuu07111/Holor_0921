using UnityEngine;
using UnityEngine.InputSystem;

public class PushingAction: MonoBehaviour
{
       [Header("Push Settings")]
    [SerializeField] private float pushDistance = 2f;      // �����鋗��
    [SerializeField] private float pushForce = 5f;         // ������
    [SerializeField] private LayerMask pushableLayer;      // ������I�u�W�F�N�g�̃��C���[

    [Header("Reference")]
    [SerializeField] private Transform playerCamera;       // �v���C���[�̎��_�i���ʕ�����Ray���΂��j


    private void Update()
    {
        // Input System�� "Push" �A�N�V������������Ă��邩���`�F�b�N
        if (Input.GetKey(KeyCode.F))
        {
            TryPushObject();
        }
    }

    /// <summary>
    /// �v���C���[�����ʂ��������I�u�W�F�N�g����������
    /// </summary>
    private void TryPushObject()
    {
        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        RaycastHit hit;

        // ���ʂ�Ray���΂��ĉ�����I�u�W�F�N�g�����o
        if (Physics.Raycast(ray, out hit, pushDistance, pushableLayer))
        {
            Rigidbody rb = hit.collider.attachedRigidbody;
            if (rb != null)
            {
                // Ray�����������ʂ̖@�������ɉ���
                // �i�܂�v���C���[���ǂ̕������瓖���������ŉ������������܂�j
                Vector3 pushDir = hit.normal * -1f; // �@���̋t�����i=�v���C���[���猩�đO�����j
                rb.AddForce(pushDir * pushForce, ForceMode.Impulse);

                Debug.Log($"'{hit.collider.name}' �� {pushDir} �����ɉ������I");
            }
            else
            {
                Debug.Log("Rigidbody �������̂ŉ����܂���B");
            }
        }

        //    Transform target = hit.transform;

        //    // �v���C���[�̌����ƃI�u�W�F�N�g�̐��ʂ�������x��v���Ă��邩����
        //    float angle = Vector3.Angle(playerCamera.forward, target.forward);

        //    if (angle < 45f) // ���ʂɋ߂���Ή�����
        //    {
        //        Rigidbody rb = target.GetComponent<Rigidbody>();
        //        if (rb != null)
        //        {
        //            // �v���C���[�̐��ʕ����ɗ͂�������
        //            rb.AddForce(playerCamera.forward * pushForce, ForceMode.Impulse);

        //            Debug.Log($"�I�u�W�F�N�g '{target.name}' ���������I");
        //        }
        //    }
        //    else
        //    {
        //        Debug.Log("�I�u�W�F�N�g�̐��ʂ������Ă��܂���B�����܂���B");
        //    }
        //}
    }

    private void OnDrawGizmosSelected()
    {
        if (playerCamera != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(playerCamera.position, playerCamera.forward * pushDistance);
        }
    }
}
