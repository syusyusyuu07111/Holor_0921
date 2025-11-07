using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class TitleMenu : MonoBehaviour
{
    public Canvas UiCanvas;

    public GameObject StartButton;
    public GameObject Option;

    public InputSystem_Actions input;
    public Vector2 MouseScreenPosition; // マウス座標(画面)
    public Vector2 StartPos;            // ボタン座標(画面)
    public Vector2 OptionPos;

    public float MouseDifStartButton;   // 距離(画面ピクセル)
    public float MouseDifOptionButton;

    private void Awake()
    {
        input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        input.UI.Enable();
    }

    private void OnDisable()
    {
        input.UI.Disable();
    }

    private void Start()
    {
        // ボタンの画面座標を一度だけ取得
        RectTransform s = StartButton.GetComponent<RectTransform>();
        RectTransform o = Option.GetComponent<RectTransform>();

        Camera cam;
        if (UiCanvas != null && UiCanvas.renderMode == RenderMode.ScreenSpaceOverlay) cam = null;
        else if (UiCanvas != null && UiCanvas.worldCamera != null) cam = UiCanvas.worldCamera;
        else cam = Camera.main;

        Vector3 sp1 = RectTransformUtility.WorldToScreenPoint(cam, s.position);
        Vector3 sp2 = RectTransformUtility.WorldToScreenPoint(cam, o.position);
        StartPos = new Vector2(sp1.x, sp1.y);
        OptionPos = new Vector2(sp2.x, sp2.y);
    }

    private void Update()
    {
        // マウスの画面座標
        MouseScreenPosition = input.UI.Point.ReadValue<Vector2>();

        // 距離
        MouseDifStartButton = Vector2.Distance(MouseScreenPosition, StartPos);
        MouseDifOptionButton = Vector2.Distance(MouseScreenPosition, OptionPos);
    }
}
