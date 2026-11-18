# PVP Chronicle Recorder

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Français](README.fr.md)

PVP Chronicle Recorder 是 PVP Chronicle 的可选本地录屏伴侣：进入支持的 PvP 战局后通过 OBS 自动录制，并把本机录像与战局时间轴关联，在复盘页面中同步查看视频、地图和战斗事件。

> [!IMPORTANT]
> 本插件不能独立识别战局，必须同时启用 PVP Chronicle。录像文件只保存在本机，不会上传到 PVP Chronicle 服务器。

## 主要功能

- 进入支持的纷争前线或水晶冲突战局后，通过 OBS WebSocket v5 自动开始录制。
- 正常结算后自动停止录制，并使用战局 ID 将录像关联到对应复盘。
- 在 PVP Chronicle 的“录屏回放”页中直接播放本地录像，不需要外部播放器。
- 视频、复盘时间轴和战场地图使用同一个时间游标并保持同步。
- 支持点击画面播放或暂停、前后跳转10秒、0.5×至2×倍速以及多档画质。
- 回放页面采用双栏布局：左侧播放视频，右侧显示同步地图和当前时间点事件。
- OBS 密码使用当前 Windows 用户的 DPAPI 加密保存。
- Flyleaf 播放运行库按需下载，并在解压前校验固定文件大小和 SHA-256。

## 快速开始

1. 安装并启动 XIVLauncher，确认 Dalamud 已启用。
2. 打开 **Dalamud 设置 → 测试版**。

3. 添加下面的自定义插件仓库地址：

   ```text
   https://raw.githubusercontent.com/chefiffer/dalamud-plugins/main/repo.json
   ```

4. 打开插件安装器，安装并启用 PVP Chronicle 和 PVP Chronicle Recorder。

5. 在 OBS 中启用 WebSocket 服务器，并设置密码。
6. 在游戏聊天栏输入：

   ```text
   /pvprec
   ```

7. 填写 OBS WebSocket 地址和密码，保存后点击“测试 OBS 连接”。
8. 点击“安装官方 Flyleaf 播放运行库”，等待下载、校验和解压完成。
9. 保持 OBS 开启，然后进入一场受支持的 PvP 战局进行测试。

OBS 场景、裁剪、游戏独占音频和故障排查的完整步骤见：[OBS 使用教程](./OBS使用教程.zh-CN.md)。

## 配置说明

| 选项 | 建议值 | 作用 |
| --- | --- | --- |
| 界面语言 | 自动 | 跟随系统语言，也可以在 Recorder 窗口中单独选择 |
| 自动录制 | 开启 | 收到 PVP Chronicle 的战局开始和结束事件后控制 OBS |
| OBS WebSocket 地址 | `ws://127.0.0.1:4455` | 连接同一台电脑上的 OBS；回环地址不会经过系统代理 |
| OBS WebSocket 密码 | 与 OBS 设置一致 | 只用于本机 WebSocket 身份验证，并由 DPAPI 加密保存 |
| Flyleaf FFmpeg 目录 | 使用安装按钮自动配置 | 提供本地视频解码能力 |
| 首帧校准 | 默认 `0 ms` | 修正录像首帧与战局时间轴之间的固定偏差 |

## 录屏与回放流程

```mermaid
flowchart LR
    A[PVP Chronicle<br/>识别战局和时间轴] -->|版本化 IPC| B[Recorder]
    B -->|OBS WebSocket| C[OBS 本机录制]
    C --> D[本地录像文件]
    B --> E[本地录像索引]
    D --> F[Flyleaf 本地解码]
    E --> F
    F -->|本机共享帧缓冲| G[PVP Chronicle<br/>录屏回放页面]
    A --> G
```

两个插件只通过版本化 IPC 交换战局 ID、时间和播放状态。录像像素通过本机共享帧缓冲进入 PVP Chronicle 的 ImGui 纹理，不会发送到网络，也不是用独立窗口覆盖在复盘页面上。

## 回放操作

- 点击视频画面：播放或暂停。
- `-10s` / `+10s`：视频、地图和复盘时间同时前后跳转10秒。
- “对齐当前复盘时间”：把视频跳转到复盘时间轴当前选择的位置。
- 倍速：支持0.5×、1×、1.5×和2×。
- 画质：支持流畅、高清、清晰和原画；默认使用原画。
- 离开录屏分页或关闭复盘窗口：播放器自动暂停，后台不会继续播放声音。

## 本地数据与隐私

| 数据 | 保存位置 | 是否上传服务器 |
| --- | --- | --- |
| OBS 录像 | 用户在 OBS 中配置的录像目录 | 否 |
| 录像与战局关联索引 | Dalamud 插件配置目录 | 否 |
| OBS WebSocket 密码 | Dalamud 插件配置，DPAPI 加密 | 否 |
| Flyleaf FFmpeg 运行库 | Recorder 插件配置目录 | 否 |
| 战局分析数据 | 由 PVP Chronicle 按其自身规则处理 | Recorder 不负责上传 |

## 已知边界

- 游戏崩溃或强制结束时，PVP Chronicle 可能收不到正常的战局结束事件，因此无法保证自动停止和关联本场录像。
- 当前不提供崩溃前后多段录像的自动拼接；下一场新战局会重新建立独立录像。
- 移动或删除 OBS 录像后，历史战绩仍然存在，但对应回放无法打开。
- Recorder 未启用时，PVP Chronicle 的采集和普通复盘不受影响。
- PVP Chronicle 未启用时，Recorder 收不到战局事件，不会自动录制。

## 第三方组件

- [Flyleaf](https://github.com/SuRGeoNix/Flyleaf)：本地视频解码和播放。
- [FFmpeg](https://ffmpeg.org/)：由 Flyleaf 官方运行库提供。
- [SharpCompress](https://github.com/adamhathcock/sharpcompress)：解压经过校验的运行库压缩包。

具体许可和来源见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。
