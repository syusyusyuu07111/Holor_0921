using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 絵そのもののスクリプト
/// ・Drop() で落とす
/// ・IsDropped / DroppedTime で「落ちているかどうか」を管理
/// </summary>
public class Painting : MonoBehaviour
{
    public static readonly List<Painting> PaintingAll = new();

    private Rigidbody _rb;

    /// <summary>この絵が落ちたかどうか</summary>
    public bool IsDropped { get; private set; } = false;

    /// <summary>何秒目に落ちたか（Time.time）</summary>
    public float DroppedTime { get; private set; } = -1f;

    private void OnEnable() => PaintingAll.Add(this);
    private void OnDisable() => PaintingAll.Remove(this);

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            Debug.LogWarning($"[Painting:{name}] Rigidbody がアタッチされていません。物理で落ちません。");
        }
    }

    /// <summary>
    /// 絵画を落とす
    /// </summary>
    public void Drop()
    {
        if (_rb == null)
        {
            Debug.LogWarning($"[Painting:{name}] Rigidbody がないので Drop できません。");
            return;
        }

        _rb.isKinematic = false;
        IsDropped = true;
        DroppedTime = Time.time;

        Debug.Log($"[Painting:{name}] Drop() 呼ばれました。IsDropped={IsDropped}, DroppedTime={DroppedTime}");
    }
}
