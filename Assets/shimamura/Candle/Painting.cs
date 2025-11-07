using UnityEngine;
using System.Collections.Generic;

public class Painting : MonoBehaviour
{

    public static readonly  List<Painting> PaintingAll = new();

    private Rigidbody _rb;

    private void OnEnable() => PaintingAll.Add(this);
    private void OnDisable() => PaintingAll.Remove(this);


    void Start()
    {
        _rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// ŠG‰æ‚ð—Ž‚Æ‚·
    /// </summary>
    public void Drop()
    {
        Debug.Log("•¿‚ªŒÄ‚Î‚ê‚½");
        if(_rb == null)
        {
            Debug.Log($"{name}‚ÉRigidbody‚ª‚È‚¢");
            return;
        }
        _rb.isKinematic = false;
    }
}
