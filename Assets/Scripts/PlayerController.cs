using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerController : MonoBehaviour
{
    InputSystem_Actions input;

    private void Awake()
    {
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        //ˆÚ“®==================================================================================-
        if(input.Player.Move.triggered)
        {

        }
    }
}
