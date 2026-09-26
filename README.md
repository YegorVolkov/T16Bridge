# T16Bridge

<p align="center">
  <img src="assets/t16000m-dual.webp" alt="Dual Thrustmaster T.16000M joysticks" width="700">
</p>

**T16Bridge** makes two identical **Thrustmaster T.16000M** joysticks appear as two independent virtual controllers: `T16LEFT` and `T16RIGHT`.

## 1. Problem

Some games identify both T.16000M sticks only by the same VID/PID/name and cannot reliably distinguish LEFT from RIGHT.

That means two physical sticks can collapse into one logical device or share bindings incorrectly.

## 2. Solution

T16Bridge creates two separate virtual joysticks:

```text
Physical LEFT  T.16000M -> T16LEFT
Physical RIGHT T.16000M -> T16RIGHT
```

The physical sticks are hidden from games with **HidHide**, while T16Bridge keeps access to them and forwards their input to the virtual devices.

## 3. How it works

On first launch:

1. T16Bridge detects the connected T.16000M devices.
2. You assign them as **LEFT** and **RIGHT** using the built-in input test.
3. T16Bridge creates `T16LEFT` and `T16RIGHT` through HIDMaestro.
4. HidHide automatically allow-lists T16Bridge, hides the two physical sticks, and enables device hiding.
5. Games see the two independent virtual controllers instead of the original identical devices.

Supported input:

- X / Y
- Rz / twist
- Slider
- 16 buttons
- POV hat

Device mapping is saved in `%LOCALAPPDATA%\T16Bridge`.

## 4. Curves

T16Bridge includes a graphical sensitivity-curve editor for every axis on both virtual devices.

Features:

- independent LEFT / RIGHT curves;
- live input/output preview;
- draggable control points;
- add/remove/reset points;
- copy a curve between axes or devices;
- mirrored center-to-edge curves for X/Y/Rz;
- no forced deadzone.

Curve settings are saved automatically in `%LOCALAPPDATA%\T16Bridge`.

## 5. Components & automation

The installer deploys the complete required stack:

| Component | Purpose |
| --- | --- |
| **T16Bridge** | Physical-to-virtual bridge and curve editor |
| **HIDMaestro v1.9.0** | Creates `T16LEFT` / `T16RIGHT` virtual HID devices |
| **HidHide v1.5.230** | Hides the two physical T.16000M devices from games |
| **.NET runtime** | Bundled self-contained with T16Bridge |

Installation is designed to be automatic:

```text
Download setup
   -> Agree & Install
   -> first-run LEFT/RIGHT identification
   -> automatic HidHide configuration
   -> done
```

The installer also creates **T16Bridge** shortcuts on the Desktop and in the Start Menu, plus an **Uninstall T16Bridge** Start Menu shortcut.

## 6. Uninstall

Use either:

```text
Start Menu -> T16Bridge -> Uninstall T16Bridge
```

or:

```text
Settings -> Apps -> Installed apps -> T16Bridge -> Uninstall
```

The uninstaller removes the full T16Bridge stack created by the installer:

- `T16LEFT` and `T16RIGHT`;
- HIDMaestro virtual-controller/driver installation;
- T16Bridge HidHide rules;
- HidHide;
- `%LOCALAPPDATA%\T16Bridge` settings;
- program files and shortcuts.

A Windows restart may be required after driver removal.

## 7. Licenses

T16Bridge is distributed under the **MIT License**.

Third-party components:

- **HIDMaestro** — MIT License
- **HidHide** — MIT License

The installer shows all required components before installation and links to the upstream projects/licenses. Exact third-party license texts are bundled with each release.

See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for details.

## Build

Requirements for contributors: Windows x64, .NET 10 SDK, PowerShell, and Inno Setup 6.

```powershell
.\scripts\build-release.ps1
```

Output:

```text
artifacts\installer\T16BridgeSetup-x64.exe
```
