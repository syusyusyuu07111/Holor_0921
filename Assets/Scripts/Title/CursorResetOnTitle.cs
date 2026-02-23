/*
このスクリプトは、タイトルシーン開始時に
マウスカーソルの状態をリセットするためのものです。

主な役割
・カーソルを表示状態にする
・カーソルのロックを解除する

ゲーム本編中に
Cursor.lockState = Locked
Cursor.visible = false
などにしている場合、

タイトルに戻ったときに
「カーソルが見えない」「動かない」状態になるのを防ぐためのリセット用スクリプトです。
*/

using UnityEngine;

public class CursorResetOnTitle : MonoBehaviour
{
    /// <summary>
    /// タイトルシーン開始時にカーソル状態を初期化する
    /// </summary>
    private void Start()
    {
        // カーソルを表示する
        Cursor.visible = true;

        // カーソルのロックを解除する（自由に動かせる状態にする）
        Cursor.lockState = CursorLockMode.None;
    }
}