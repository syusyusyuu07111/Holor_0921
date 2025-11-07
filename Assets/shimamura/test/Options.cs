using UnityEngine;


public class Options : MonoBehaviour
{

    [SerializeField] private GameObject _optionsPanel;          // オプションのパネル

    private Canvas canvas;

    private void Start()
    {

    }

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Y))
        {
            if (!_optionsPanel.activeSelf)  OptionsOpen();

            else OptionsClose();
        }
    }

    void OptionsOpen()
    {
        _optionsPanel.SetActive(true);
        Time.timeScale = 0;
    }
    void OptionsClose()
    {
        _optionsPanel.SetActive(false);
        Time.timeScale = 1f;
    }
}
