# VR 化の準備（Meta Quest 単体）

ブランチ `vr-setup` で、Meta Quest 単体（PC なし）で遊べる VR 版の土台を入れた。デスクトップ版の試作（`Prototype.unity`）はそのまま残している。

## 方針

- 対象: Meta Quest 3 / 3S / 2（Android アプリ）
- 部品: Unity 公式の **OpenXR + XR Interaction Toolkit（XRI）**
- 視点: **等身大の一人称**（自分の実際の背の高さで部屋を歩く）。小人サイズにしたくなったら `VRSceneBuilder.cs` の `PlayerScale` を下げる（0.15 で身長 25cm ほど。未検証）

## Unity の版

**6000.7.0b2（Unity 6.7 ベータ）に上げた。** 元は 6000.6.5f1 だったが、作業した Mac に 6.6 がなかったため。チームで開く人は全員 6000.7.0b2 に、**Android Build Support（OpenJDK・SDK & NDK 込み）付き**でそろえる必要がある。

上げたときに Unity が自動で変えたもの: URP 17.6 → 17.7、Input System 1.20 → 6.7、Test Framework・Timeline・uGUI など。XRI は 6.7 に合わせて 3.7.0-pre.1 になった。

## 追加したもの

| 場所 | 中身 |
| --- | --- |
| `Assets/Scripts/VR/VRDoorHandle.cs` | VR でドアを開け閉めする（ドアに触れるかレイを当ててグリップ） |
| `Assets/Scripts/VR/Editor/VRProjectSetup.cs` | Quest 用のプロジェクト設定・APK ビルド（メニュー） |
| `Assets/Scripts/VR/Editor/VRSceneBuilder.cs` | VR シーンの生成（メニュー） |
| `Assets/Scenes/VR_Prototype.unity` | 生成した VR シーン（試作と同じ部屋 + XR Origin） |
| `Assets/Samples/XR Interaction Toolkit/` | XRI のサンプル（Starter Assets・XR Interaction Simulator） |
| `Assets/XR/`, `Assets/XRI/` | XR Plug-in Management・OpenXR・XRI の設定 |

`PrototypeSceneBuilder.cs` は、部屋を作る部分を `CreateRoom()` として切り出し、VR シーンからも使えるようにした（デスクトップ版の動きは同じ）。

## メニュー「BanjiRun/VR」

1. **Import XRI Samples** — XRI のサンプルを取り込む（済み）
2. **Setup Quest Project** — Quest 用の設定（済み。何度実行してもよい）
3. **Build VR Scene** — `VR_Prototype.unity` を作り直す（ビルドするシーンにも登録する）
4. **Build Quest APK** — `Builds/Quest/BanjiRun.apk` を作る

## Quest 用の設定（2. でやること）

- Android: パッケージ名 `com.DefaultCompany.BanjiRun`（仮）、最低 API 32、IL2CPP、ARM64、Vulkan のみ、横向き（左）、ASTC、Game Activity
- XR Plug-in Management: **Android だけ** OpenXR を有効（Mac のエディターでは XR を起動しない）
- OpenXR（Android）: Meta Quest Support、Oculus Touch / Meta Quest Touch Plus のコントローラー、Single Pass Instanced（マルチビュー）
- 画質（Android は Mobile 段階 = `Mobile_RPAsset`）: 解像度 1.0、MSAA 4x、HDR オフ
- XRI: Interaction Layer 31 を「Teleport」に、XR Interaction Simulator をエディターの再生時だけ出す

## 試し方

**Mac のエディターで**: `VR_Prototype.unity` を開いて再生すると、XR Interaction Simulator が出て、キーボードとマウスで頭と両手を動かせる（操作は画面の表示を参照）。Mac は Quest Link に対応していないので、実機の映像をエディターに出すことはできない。

**Quest 実機で**: Quest を開発者モードにして USB でつなぎ、Build Profiles（Android）から Build And Run。または 4. で APK を作り、Meta Quest Developer Hub で入れる。

## VR での操作（XRI の初期設定）

- 左スティック: 移動
- 右スティック 左右: 45° 回転 / 前に倒して離す: テレポート（床だけ。`VRSceneBuilder.TeleportFloors`）
- 右 A ボタン: ジャンプ
- グリップ: ドアの開け閉め（ドアに触れるか、レイを当てて）
- 動いている間は視界の端が暗くなる（酔い対策のビネット）

## まだやっていないこと・気になる点

- **重さ**: 部屋のリアルタイムライトが 78 個、オブジェクトが約 7,500 個、すべてのメッシュに MeshCollider。Quest では重いはずなので、ライトのベイク（Mixed/Baked + ライトマップ）、静的バッチ、コライダーの簡略化が要る
- **実機での確認**: APK のビルドまで。Quest で動かして、フレームレート・酔い・大きさの感じは見ていない
- **ゲームの中身**: 試作のダッシュ・ジャンプ・E キーのドアは、デスクトップ版のまま。VR での遊び（目的、敵、UI）はこれから。操作説明が画面の文字（OnGUI）なので、VR ではワールド空間の UI にする必要がある
- **出現位置**: VR 版はホールの床から始まる。モデルには `PlayerSpawn_Elevator`（エレベーター内の出現位置）と `TitleCamera_Elevator` があるので、タイトル画面やスタート地点に使える
- **OpenXR の検証で残った警告**: PC 用の Renderer（`PC_Renderer`）の SSAO。Android は Mobile 段階しか使わないので Quest には影響しないはず
- パッケージ名 `com.DefaultCompany.BanjiRun` は仮。ストアに出す前に決める
