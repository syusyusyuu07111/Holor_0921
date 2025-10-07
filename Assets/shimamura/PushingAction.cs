using UnityEngine;
using UnityEngine.InputSystem;

public class PushingAction: MonoBehaviour
{
    [SerializeField]
    private float _pushSpeed = 2f;//�������Ƃ��̈ړ��X�s�[�h
    private float _pushDistance = 0;//�肪�͂�����

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnPushActoin(InputAction.CallbackContext context)
    {
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, _pushDistance))
        {
            if (hit.collider.CompareTag("Pushable"))
            {
                // ���������Ɉړ�������
                hit.collider.transform.Translate(transform.forward * _pushSpeed * Time.deltaTime, Space.World);
            }
        }
    }
}
