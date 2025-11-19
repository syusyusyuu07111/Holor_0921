using UnityEngine;
using System.Collections.Generic;
using CriWare;  // 必要なら

/// <summary>
/// 絵そのもののスクリプト
/// ・Drop() で落下開始
/// ・着地した瞬間に IsDropped / DroppedTime を更新
/// </summary>
public class Painting : MonoBehaviour
{
    public static readonly List<Painting> PaintingAll = new();

    private Rigidbody _rb;

    /// <summary>この絵が落ちたかどうか（＝床に着地したか）</summary>
    public bool IsDropped { get; private set; } = false;

    /// <summary>何秒目に落ちたか（Time.time）</summary>
    public float DroppedTime { get; private set; } = -1f;

    [Header("着地時SE用 CRI Atom Source")]
    [SerializeField] private CriAtomSource _landSeSource;

    [Header("床判定用のタグ名（例：Floor）")]
    [SerializeField] private string _floorTag = "Floor";

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
    /// 絵画を落下させる（物理ONするだけ）
    /// </summary>
    public void Drop()
    {
        if (_rb == null)
        {
            Debug.LogWarning($"[Painting:{name}] Rigidbody がないので Drop できません。");
            return;
        }

        // まだ落下開始していないときだけ有効化
        if (_rb.isKinematic)
        {
            _rb.isKinematic = false;
            Debug.Log($"[Painting:{name}] Drop() 呼ばれました。落下開始。");
        }
    }

    /// <summary>
    /// 床にぶつかった瞬間を「落ちた」とみなす
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        // すでに落下判定済みなら何もしない（多重再生防止）
        if (IsDropped) return;

        // タグで床かどうかを判定（タグを使わないならこの if 自体を消してOK）
        if (!string.IsNullOrEmpty(_floorTag) &&
            !collision.gameObject.CompareTag(_floorTag))
        {
            return;
        }

        // ここで初めて「落ちた」判定
        IsDropped = true;
        DroppedTime = Time.time;

        // SE 再生
        if (_landSeSource != null)
        {
            _landSeSource.Play();
        }
        else
        {
            Debug.LogWarning($"[Painting:{name}] _landSeSource が設定されていません。着地SEは鳴りません。");
        }

        Debug.Log($"[Painting:{name}] 床に着地しました。IsDropped={IsDropped}, DroppedTime={DroppedTime}");
    }
}
