using UnityEngine;

public class PlayerChase : MonoBehaviour
{
    public Transform Player;      // プレイヤー参照
    public Transform Ghost;       // ゴースト参照
    public float moveSpeed = 3.0f;
    public float stopDistance = 0f;

    private bool isChasing = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            isChasing = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            isChasing = false;
    }

    private void Update()
    {
        if (!isChasing || Player == null || Ghost == null) return;

        Vector3 toPlayer = Player.position - Ghost.position;
        toPlayer.y = 0f; // 水平だけ追うなら

        float dist = toPlayer.magnitude;
        if (dist > stopDistance)
        {
            // プレイヤー方向へ回転
            if (toPlayer != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(toPlayer);
                Ghost.rotation = Quaternion.Slerp(Ghost.rotation, targetRot, 0.1f);
            }
            // 前進
            Ghost.position += Ghost.forward * moveSpeed * Time.deltaTime;
        }
    }
}
