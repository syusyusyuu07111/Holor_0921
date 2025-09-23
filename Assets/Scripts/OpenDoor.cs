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
        if ((DistanceDoor < OpenDistance)&&CanOpen==false)
        {
            //タイムラインを再生
            Directer.playableAsset = Open;
            Directer.playOnAwake = true;
            Directer.Play();
            CanOpen = true;
        }
        if(CanOpen&& (DistanceDoor > OpenDistance))
        {
            Debug.Log("離れました");
            //タイムラインを逆再生
            Directer.playableAsset = Open;
            Directer.playOnAwake = true;
            Directer.time = Directer.duration;//最後の時間に飛ばす
            Directer.Evaluate();
            Directer.Play();
            Directer.playableGraph.GetRootPlayable(0).SetSpeed(-1);
            CanOpen = false;
        }
    }
}
