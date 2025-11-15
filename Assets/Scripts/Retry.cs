using UnityEngine;
using UnityEngine.SceneManagement;

public class Retry : MonoBehaviour
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


    // Update is called once per frame
    void Update()
    {
        if(input.Player.Attack.triggered)
        {
            SceneManager.LoadScene("SampleScene");
        }
    }
}
