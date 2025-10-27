# Ghost Animator Avatar Setup

- 開く対象: `Assets/Prefabs/ChaseObj.prefab`
- 必須設定: `Animator` コンポーネントの `Avatar` フィールドに FBX モデルから生成された Avatar (`Ghost_FBX_1026Avatar`) を設定する。
- Avatar を確認するには、インポートした FBX ファイルを選択し、Inspector の **Rig** タブで `Avatar Definition` を `Create From This Model` に設定して Apply する。
- Avatar が割り当てられると、`Ghost_Chase_Loop`、`Ghost_Search_Loop`、`Ghost_gameOver_End` のアニメーションステートが再生されるようになる。

## トラブルシューティング
- Avatar が見つからない場合は、FBX の Rig 設定を `Humanoid` または `Generic` に再設定して Apply する。
- それでも動かない場合は、シーン上の幽霊オブジェクトの Animator 参照が Prefab と一致しているか確認する。
