using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class LightOff : MonoBehaviour
{
    public GameObject Player;
    public GameObject Light;
    public float PushDistance = 3.0f;
    public bool OnLight = true;
    public GameObject Ghost;

    //ライトオブジェクト
    [SerializeField] private List<Light> LightLists = new();

    InputSystem_Actions input;

    public TextMeshProUGUI text;

    private void Awake()
    {
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }
    void Update()
    {


        //ライトを押したら全部の　ライトを消す関数呼び出す-------------------------------------------------------------------------
        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        //ライトオン　オフのテキスト表示
        if (distance < PushDistance)
        {
            text.gameObject.SetActive(true);
        }
        else
        {
            text.gameObject.SetActive(false);
        }

        //ライトの近く　ボタン押す　ライトがついてるフラグになってるとき
        if (distance < PushDistance && input.Player.Interact.triggered && OnLight == true)
        {
            Off();
        }
        else if (distance < PushDistance && input.Player.Interact.triggered && OnLight == false)
        {
            On();
        }
    }
    //全部のライトを消す---------------------------------------------------------------------------------------------------------
    void Off()
    {
        foreach (Light light in LightLists)
        {
            light.enabled = false;
            OnLight = false;
            Debug.Log("きえた");
            Destroy(Ghost.gameObject);
        }
    }
    //ライトをオンにする--------------------------------------------------------------------------------------------------------------
    void On()
    {
        foreach (Light light in LightLists)
        {
            light.enabled = true;
            OnLight = true;
        }
    }
}
