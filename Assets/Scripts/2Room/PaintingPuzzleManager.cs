using UnityEngine;

/// <summary>
/// ŠGƒpƒYƒ‹‘S‘Ì‚Ìƒtƒ‰ƒOŠÇ—i³‰ğ‚ÌŠG2–‡•ªj
/// </summary>
public class PaintingPuzzleManager : MonoBehaviour
{
    [Header("³‰ğ‚ÌŠGA‚ğE‚Á‚½‚©")]
    public bool pickedUpPaintingA = false;

    [Header("³‰ğ‚ÌŠGB‚ğE‚Á‚½‚©")]
    public bool pickedUpPaintingB = false;

    /// <summary>
    /// ³‰ğ2–‡‚Æ‚àE‚Á‚½‚©‚Ç‚¤‚©
    /// </summary>
    public bool AllCorrectPickedUp =>
        pickedUpPaintingA && pickedUpPaintingB;
}
