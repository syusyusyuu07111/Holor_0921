using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.Playables;
using UnityEngine.InputSystem;

public class OpenDoor : MonoBehaviour
{
    public bool CanOpen;
    public Transform Player;
    public Transform Door;
    float DistanceDoor;
    public float OpenDistance = 1.5f;
    [Header("Timeline")]
    public TimelineAsset Open;
    public PlayableDirector Directer;

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
        CanOpen = false;
        Directer = GetComponent<PlayableDirector>();
        Directer.playOnAwake = false;
    }
    void Update()
    {
        DistanceDoor = Vector3.Distance(Player.transform.position, Door.transform.position);
        Debug.Log(DistanceDoor);
        if (DistanceDoor < OpenDistance)
        {
            //ƒ^ƒCƒ€ƒ‰ƒCƒ“‚ðÄ¶
            Directer.playableAsset = Open;
            Directer.playOnAwake = true;
            Directer.Play();
        }
    }
}
