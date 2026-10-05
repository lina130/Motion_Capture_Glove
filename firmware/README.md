# Firmware (固件)

STM32CubeIDE projects for the motion-capture glove palm boards.

| Project | MCU | Transport | Notes |
| --- | --- | --- | --- |
| `Agilereach_STM32H523_I2C_V2_IMU-Glove/` | STM32H523 | I2C IMU front-end | IMU acquisition + Mahony filter. |
| `Agilereach_STM32H70B_WIFI_6IT-Glove/` | STM32H7B0 | USB-CDC + Wi-Fi 6 | Palm board, `GLOVE_PROJECT.md` design doc, Python `tools/`. |

Each project contains the CubeMX `.ioc` configuration, `Core/` application code,
vendored `Drivers/`/`Middlewares/` and the CubeIDE project metadata. Compile with
STM32CubeIDE; the `Debug/` build folder is not tracked.

## Python host tooling

`Agilereach_STM32H70B_WIFI_6IT-Glove/tools/usb_cdc_monitor/` contains the serial
monitor, frame-alignment, palm-parser, pipeline and Phase-0 auto-detection scripts used
during bring-up (see its own `README.md`).

## Third-party components

STMicroelectronics HAL/CMSIS/USB-Device and ST `MotionFX` are vendored and remain
under their original ST license terms.