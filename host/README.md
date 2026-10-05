# Host software (上位机)

Unity projects that receive motion-capture glove frames and animate a 3D hand.

- `multisensor/` - multi-sensor glove build.
- `onlytip/` - tip-only glove build.

Requirements: **Unity 2022.3.62f3c1**.

## Key scripts (per project)

| Script | Role |
| --- | --- |
| `Assets/Scenes/SerialReceiver.cs` | Reads the serial / USB-CDC frame stream. |
| `Assets/Scenes/HandMotionManager.cs` | Maps glove data onto the hand rig. |
| `Assets/Scenes/FingerSolver.cs` | Converts finger quaternions to joint rotations. |
| `Assets/Scenes/HandAntiClipping.cs` | Keeps the rendered hand from intersecting itself. |
| `Assets/Scenes/CameraController.cs` | Orbit / framing camera. |
| `Assets/ForceGridVisualizer.cs` | Optional force-grid visualisation. |

Open either project folder with Unity Hub. Generated `Library/` and IDE project files
are not tracked.

See `firmware/Agilereach_STM32H70B_WIFI_6IT-Glove/GLOVE_PROJECT.md` for the exact
host <-> device data contract.