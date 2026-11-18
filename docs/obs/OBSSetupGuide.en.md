# PVP Chronicle Recorder · OBS setup guide

This guide explains how to configure OBS for automatic PvP recording with PVP Chronicle Recorder and how to solve connection failures, wrong-window capture, black borders, oversized files, and unwanted desktop audio.

> [!IMPORTANT]
> Recorder only controls OBS and links local recordings. Install and enable PVP Chronicle, PVP Chronicle Recorder, and OBS Studio before starting.

## 1. Enable OBS WebSocket

1. Start OBS.
2. Open **Tools → WebSocket Server Settings**.
3. Enable the WebSocket server.
4. Keep the default port `4455` and set a password used only on this computer.
5. Save the settings and leave OBS running.

Do not forward port 4455 to the internet or open it to other computers in the firewall. Recorder only needs the loopback address when OBS runs on the same computer.

Enter `/pvprec` in game and fill in:

```text
OBS WebSocket address: ws://127.0.0.1:4455
OBS WebSocket password: the password configured in OBS
```

Select **Save settings**, then **Test OBS connection**. The expected result is **OBS WebSocket connected**.

## 2. Create a dedicated recording scene

Create a separate OBS scene such as “FFXIV PvP Recording” so webcams, browser sources, and streaming alerts are not included.

### Option A: Game Capture

1. Select `+` under Sources and add **Game Capture**.
2. Set Mode to **Capture specific window**.
3. Select the window titled “FINAL FANTASY XIV” from `ffxiv_dx11.exe`.
4. Check the preview.

If OBS repeatedly locks onto the wrong window, delete and recreate the source instead of relying on automatic matching, or use Display Capture.

### Option B: Display Capture

Display Capture is more reliable but records everything visible on that display. Select the monitor running FFXIV, then crop the source to the actual game area.

## 3. Remove black borders without changing aspect ratio

`px` means pixels. For example, if a `1920×1080` canvas has a `120 px` black border at the bottom, the active height is:

```text
1080 - 120 = 960 px
```

The active image is therefore `1920×960`:

1. Select the game or display source in OBS.
2. Right-click and open **Transform → Edit Transform**.
3. Set **Crop: Bottom** to `120`.
4. Open **Settings → Video**.
5. Set **Base (Canvas) Resolution** to `1920×960`.
6. Set **Output (Scaled) Resolution** to `1920×960`, or a lower resolution with the same aspect ratio.
7. Return to the source and select **Transform → Fit to Screen**.

You can also hold `Alt` and drag a source edge, but exact pixel values are easier to reproduce.

Do not use **Stretch to Screen**, which distorts characters and the map. Black space outside the OBS canvas is not recorded; black borders inside the canvas are. Record a 5–10 second test and inspect the actual file.

## 4. Record game audio only

1. Open **Settings → Audio**.
2. Disable Desktop Audio, Desktop Audio 2, and any unwanted microphones.
3. Add **Application Audio Capture (BETA)** under Sources.
4. Select the “FINAL FANTASY XIV” window from `ffxiv_dx11.exe`.
5. Play game audio and browser audio at the same time.

The game-audio meter should move, while the browser, Discord, system notifications, and microphone should not be recorded.

After a crash, the old `ffxiv_dx11.exe` process disappears. OBS usually binds to the new process automatically. If audio does not return, select the new FFXIV window again.

## 5. Set recording format and file size

| Setting | Recommended value | Reason |
| --- | --- | --- |
| Recording format | MKV | Less likely than MP4 to become entirely unusable after an OBS or game crash |
| Video encoder | Hardware NVENC H.264, when available | Reduces game CPU load and offers good compatibility |
| Recording quality | High Quality, Medium File Size | Suitable for review without lossless recording |
| Audio bitrate | 128 or 160 Kbps | Clear enough for game audio |
| Frame rate | 30 FPS | Usually sufficient for review and significantly smaller |

At a fixed 6000 Kbps video bitrate plus 160 Kbps audio, each minute is about 46 MB. To reduce size:

- lower video bitrate to 3500–5000 Kbps; or
- use CQP in Advanced Output mode and start with `CQ 24`—higher values mean smaller files and lower quality;
- keep 30 FPS instead of 60 FPS;
- encode only the active area instead of including black borders.

After each change, record one minute containing movement and combat effects, then judge the actual file size and text clarity.

## 6. Configure Recorder

Enter `/pvprec` in game:

1. Select Auto or a preferred interface language.
2. Enable automatic recording for supported PvP matches.
3. Enter `ws://127.0.0.1:4455` under **OBS WebSocket address**.
4. Enter the OBS password under **OBS WebSocket password**.
5. Normally leave **Flyleaf FFmpeg runtime folder** empty and use the install button.
6. Keep **First-frame calibration** at `0 ms` initially.
7. Save, then test the OBS connection.
8. Install the official Flyleaf playback runtime.
9. Wait for download, SHA-256 verification, and extraction to finish.

First-frame calibration corrects a consistent offset between the video and match timeline. Keep it at `0 ms` unless multiple tests show the same offset in the same direction.

Each of the four main settings has a name and explanation above its input, even when the input is empty.

Replay quality only scales the current decoded frame. It does not transcode or create another video. Switching between Smooth, HD, Clear, and Original leaves no proxy-video cache on the C drive. Memory frames are released when replay closes; the OBS recording and installed FFmpeg runtime remain.

## 7. Game crashes and reconnects

Recorder does not automatically join videos made before and after a crash.

- Normal result: the plugin receives the end event, stops OBS, and links the recording.
- Game crash: the end event may be missing, leaving OBS and local recovery state out of sync.
- Reconnecting to the same match: recording is not guaranteed to restart, and separate videos are not joined.
- Next match: a new, independent recording should be created.

After a crash, check whether OBS is still recording. Stop it manually if it is recording the login screen. Keep the existing recording if you may need it.

## Security and privacy

- Never expose the OBS WebSocket port to the public internet.
- Never include the OBS password in screenshots, issues, or logs.
- Recordings may contain character names, chat, notifications, and desktop information. Review and edit them before sharing.
- Recorder does not upload recordings. Files you manually upload to cloud storage or GitHub are outside the plugin's control.
