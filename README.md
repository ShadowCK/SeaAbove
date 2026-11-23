# Upside Down Fishing（反重力钓鱼）— PICO MR

把真实房间的天花板变成一片倒置的海，向上甩竿，把鱼从天花板里钓出来，让它们漂浮在房间里。

- Unity **6000.3.26f1** · URP · PICO Unity SDK (XR, `picoxr` 模式) · 目标设备 PICO（Android）
- 包名 `com.xicheng.upsidedownfishing`
- 所有美术目前都是**程序化生成**（鱼、鱼竿、海床、装饰、音效），之后可以逐个换成正式美术资源

## 快速开始

1. 用 Unity 6000.3.26f1 打开工程（Build Target = Android）。
2. 菜单 **UpsideDownFishing → Setup Project**（首次已自动执行过；重复执行是安全的）。
3. 打开 `Assets/UpsideDownFishing/Scenes/Main.unity` 点 Play，用键鼠模拟器试玩。
4. 真机：**UpsideDownFishing → Build Android APK**，然后 `adb install`。第一次运行如果系统里没有房间数据，会拉起 PICO 的房间扫描。

## 操作

| 操作 | PICO 手柄 | 编辑器模拟 |
|---|---|---|
| 抛竿 | 按住右扳机，向上甩竿，松开 | 按住左键，鼠标快速上划，松开 |
| 收线 / 拉鱼 | 按住右扳机（拉鱼时向下压竿更有效） | 按住左键 |
| 提竿（鱼咬钩时） | 快速向下拉竿 或 扣扳机 | 左键 |
| 切换鱼竿 | 右手 B | B |
| 图鉴 / 收集面板 | 左手 Y | Y |
| 重新扫描房间 | 左手 X | X |
| 看 / 移动 |  头显 | 右键拖动 / WASD QE |

## 玩法循环

1. **房间理解**：读取 PICO Scene Capture 数据，用 Ceiling 多边形（没有就用 Floor 轮廓）生成水面，高度取天花板顶点平均值；家具 Box3D 变成碰撞体和深度遮挡体。
2. **天花板之海**：水面（卡通波纹、焦散、岸边泡沫、涟漪）+ 天花板“上方”的海体积 + 倒挂的海草和礁石 + 往下飘的气泡。海里的鱼是**肚皮朝上**游的。
3. **钓鱼**：浮标“向上掉”（反重力），挂在水面下方；鱼会接近、试咬（轻震动），真正咬钩时浮标被拽进水里，1 秒内提竿。
4. **搏鱼**：张力条要保持在绿色区间——太紧 0.6 秒会断线，太松 3 秒鱼会脱钩；鱼的体力耗尽后很好收。把鱼拉过水面即捕获。
5. **漂浮**：钓出来的鱼翻正身体，在房间里慢慢游，会绕开家具、避开你的头（最多 15 条，最早的会消失）。
6. **收集**：7 种原创鱼（普通 → 传说），按累计捕获数和鱼竿等级逐步出现；3 把鱼竿（张力、收线速度、稀有度加成）；4 个漂浮在房间里的装饰品。存档保存在 PlayerPrefs。

## 代码结构（`Assets/UpsideDownFishing/`）

| 路径 | 作用 |
|---|---|
| `Scripts/Core/GameBootstrap.cs` | 场景唯一入口，找到 XR Origin，打开 VST，运行时组装所有系统 |
| `Scripts/Room/RoomUnderstanding.cs` | PICO Scene Capture → `RoomModel`（天花板多边形/高度、地面、家具）；编辑器中或没有数据时用模拟房间 |
| `Scripts/Room/RoomGeometry.cs` | 家具 → 碰撞体 + 深度遮挡（鱼可以躲到真实沙发后面） |
| `Scripts/Water/CeilingWater.cs` | 水面网格、海体积、倒置海床、气泡、涟漪 API |
| `Scripts/Fish/*` | 鱼种数据、程序化鱼模型、鱼的 AI（水中 / 咬钩 / 被钓 / 房间漂浮）、鱼群管理 |
| `Scripts/Fishing/FishingRod.cs` | 抛竿、反重力浮标、咬钩判定、张力搏鱼、收线、鱼竿弯曲和鱼线 |
| `Scripts/Collection/*` | 图鉴、金币、鱼竿和装饰品解锁、存档；房间装饰品 |
| `Scripts/UI/GameHUD.cs` | 提示、张力/体力条、捕获卡片、手腕图鉴 |
| `Scripts/Core/EditorSimulator.cs` | 没有头显时的键鼠模拟 |
| `Shaders/` | `UDF/Toon`（鱼游动摆尾）、`UDF/CeilingWater`、`UDF/WaterVolume`、`UDF/Glow`、`UDF/DepthOccluder` |
| `Editor/` | 一键 Setup、APK 构建 |

`GameBootstrap` 上的调试开关：`forceSimulatedRoom`、`debugShowFurniture`、`resetSaveOnStart`。

## 已知限制 / 下一步

- 天花板多边形的局部坐标约定（XY 还是 XZ）做了自动判断，需要真机确认。
- 鱼在房间漂浮时只绕开家具 Box3D，没有用 Spatial Mesh，墙面是用天花板轮廓近似的。
- 文字用的是旧版 TextMesh + 系统字体回退；正式版建议换成 TextMeshPro + 中文字体。
- 音效是程序合成的占位音。
- 还没做：商店（金币目前只是计数）、鱼的大小排行、多人。
