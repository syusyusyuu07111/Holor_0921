using UnityEngine;

public class GhostChase : MonoBehaviour
{
    public float speed = 5f;
    Transform player; // キャッシュ

    void Awake()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go) player = go.transform;
    }

    void Update()
    {
        if (!player) return;

        // 水平面で追う（Yは自分を維持）
        Vector3 target = new Vector3(player.position.x, transform.position.y, player.position.z);
        Vector3 dir = (target - transform.position).normalized;

        transform.position += dir * speed * Time.deltaTime;
    }
}
