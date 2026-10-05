# Motion Capture Glove

Firmware and host (PC) software for a **right-hand motion-capture glove** used to drive a
3D hand model in real time and to record reproducible hand-pose datasets for downstream
ML / analytics.

The two sides of the system are kept independent so either one can be built and used on
its own:

| Folder | Side | Description |
| --- | --- | --- |
| `host/` | Host / PC (上位机) | Unity 2022.3 projects that receive glove frames over serial and animate a 3D hand. |
| `firmware/` | Device / MCU (固件) | STM32CubeIDE projects for the two palm boards, plus the Python USB-CDC tooling. |

## System Overview

1. Finger-tip IMUs and a palm IMU stream raw 9-axis data into the STM32 palm board.
2. The board runs a Mahony attitude filter and converts the IMU frames into a compact
   palm + per-finger protocol (quaternions and derived `flex`/`swing` angles).
3. Frames are sent to the PC over USB-CDC (or Wi-Fi on the H7B0 board).
4. The Unity host reads the stream and drives the skinned hand model.

Detailed design notes, the frame conventions, the calibration procedure, and the
protocol contract live in
[`firmware/Agilereach_STM32H70B_WIFI_6IT-Glove/GLOVE_PROJECT.md`](firmware/Agilereach_STM32H70B_WIFI_6IT-Glove/GLOVE_PROJECT.md).

## `host/` - Unity host software

Two variants of the Unity project are kept because they target slightly different
hardware builds:

- `multisensor/` - multi-sensor build.
- `onlytip/` - tip-only build.

Both are Unity **2022.3.62f3c1** projects. Open the folder with Unity Hub; the generated
`Library/` cache, `*.sln` and `*.csproj` files are intentionally not tracked.

## `firmware/` - STM32 firmware

- `Agilereach_STM32H523_I2C_V2_IMU-Glove/` - STM32H523 palm board, I2C IMU front-end.
- `Agilereach_STM32H70B_WIFI_6IT-Glove/` - STM32H7B0 palm board with Wi-Fi 6 and a
  USB-CDC transport; this project also carries the Python host tooling under `tools/`.

Open the project folders with STM32CubeIDE. Build output (`Debug/`) is not tracked.

## Third-party code

The firmware vendors STMicroelectronics HAL / CMSIS / USB-Device and ST `MotionFX`
components. Those files remain under their original ST license terms; see the
`LICENSE` / `LICENSE.txt` files inside `Drivers/` and `Middlewares/`. The MIT
license below applies to the original project code only.

## License

Original project code is released under the MIT License. See `LICENSE`.