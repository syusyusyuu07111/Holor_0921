/*
このスクリプトは、絵パズル全体の進行状態を管理するためのフラグ管理クラスです。

主な役割
・正解の絵Aを拾ったかどうかを保持する
・正解の絵Bを拾ったかどうかを保持する
・正解2枚がそろったかどうかを他のスクリプトから参照できるようにする

このクラス自体は見た目や演出は一切行わず、
「状態だけ」を管理する責任を持つシンプルな管理クラスです。
*/

using UnityEngine;

/// <summary>
/// 絵パズル全体のフラグ管理（正解の絵2枚分）
/// </summary>
public class PaintingPuzzleManager : MonoBehaviour
{
    [Header("正解の絵Aを拾ったか")]
    // PaintingType.PaintingA を拾ったときに true にされる
    // 他スクリプト（PaintingPickupなど）から書き換えられる想定
    public bool pickedUpPaintingA = false;

    [Header("正解の絵Bを拾ったか")]
    // PaintingType.PaintingB を拾ったときに true にされる
    public bool pickedUpPaintingB = false;

    /// <summary>
    /// 正解2枚とも拾ったかどうかを返すプロパティ
    /// </summary>
    ///
    /// 役割：
    /// ・AとBの両方がtrueなら true を返す
    /// ・どちらか一方でもfalseなら false を返す
    ///
    /// 意図：
    /// ・外部スクリプト（例：GivePaintingToGhostなど）が
    ///   「パズルが完成しているか」を1行で判定できるようにする
    /// ・内部実装（AとBのbool）を直接触らせず、意味のある状態として提供する
    public bool AllCorrectPickedUp =>
        pickedUpPaintingA && pickedUpPaintingB;
}