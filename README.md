# T16Bridge

T16Bridge makes two identical **Thrustmaster T.16000M** joysticks appear as two independent virtual controllers and adds a graphical sensitivity-curve editor for every axis.

## End-user install

The intended release experience is:

1. Download `T16BridgeSetup-x64.exe` from GitHub Releases.
2. Run it.
3. Review the single consent screen listing every required component and its license.
4. Tick **I agree** and click **Agree & Install**.
5. Complete a possible Windows restart if HidHide requests one.
6. Start T16Bridge and identify the physical LEFT and RIGHT sticks using **Test LEFT / Test RIGHT**.

The installer installs the complete required stack:

- **T16Bridge**
- **HIDMaestro v1.9.0** — MIT
- **HidHide v1.5.230** — MIT
- the self-contained .NET runtime needed by T16Bridge

Nothing is optional in the installer; the goal is one known-good configuration.

## What T16Bridge does

```text
Physical LEFT T.16000M  -> T16Bridge -> curve -> T16LEFT
Physical RIGHT T.16000M -> T16Bridge -> curve -> T16RIGHT
```

Physical input is read through HidSharp. Virtual controllers are created through HIDMaestro.

### Supported input

- X
- Y
- Rz / twist
- Slider
- 16 buttons
- POV hat

### Curves

Each device has independent curves for:

- X
- Y
- Rz
- Slider

For X/Y/Rz the graph is **center -> edge**:

```text
0%   = physical center
100% = either edge
```

The same curve is mirrored automatically to both directions. For example:

```text
50% input -> 30% output
```

means both `+50% -> +30%` and `-50% -> -30%`.

The editor supports drag, double-click to add a point, Reset Linear, live input/output display, and copying a curve from any LEFT/RIGHT axis.

## First run

T16Bridge detects connected T.16000M devices and opens the assignment dialog.

For each side:

1. Select a candidate device.
2. Click **Test LEFT** or **Test RIGHT**.
3. Move that physical stick within 5 seconds.
4. `INPUT DETECTED` confirms the device.
5. Save the assignment.

Per-user settings are stored in:

```text
%LOCALAPPDATA%\T16Bridge\curves.json
%LOCALAPPDATA%\T16Bridge\device-mapping.json
```

## Build locally

Requirements for contributors:

- Windows 10/11 x64
- .NET 10 SDK
- PowerShell
- Inno Setup 6 (only needed for the final installer)

Prepare pinned third-party dependencies:

```powershell
.\scripts\prepare-deps.ps1
```

Build the app:

```powershell
dotnet build .\src\T16Bridge\T16Bridge.csproj
```

Build the complete release installer:

```powershell
.\scripts\build-release.ps1
```

Output:

```text
artifacts\installer\T16BridgeSetup-x64.exe
```

## GitHub release

Push a version tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

GitHub Actions will:

1. download pinned HIDMaestro and HidHide releases;
2. preserve their upstream license texts;
3. publish T16Bridge as a self-contained `win-x64` application;
4. build `T16BridgeSetup-x64.exe`;
5. upload it to the GitHub Release.

## Third-party software

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

HIDMaestro and HidHide are both MIT-licensed upstream projects. Their exact license files are bundled in every release installer. T16Bridge itself is MIT licensed.

## Current scope

The installer installs all required software. T16Bridge handles LEFT/RIGHT device identification and curves. Automatic HidHide device-rule configuration is the next integration step; until that is implemented, HidHide may still need its physical T.16000M hide rules configured once after installation.

## Automatic HidHide configuration

After LEFT/RIGHT device assignment, T16Bridge automatically configures HidHide:

1. Adds the running `T16Bridge.exe` to the HidHide application allow-list.
2. Adds both selected physical T.16000M device instance IDs to the HidHide hidden-device list.
3. Forces normal allow-list mode (`inv-off`).
4. Enables device hiding (`cloak-on`).

This is also re-applied on later launches, so a completed installation normally requires no manual HidHide setup.

If HidHide was just installed and its driver is not active yet, T16Bridge shows a warning asking for a Windows restart. The bridge itself remains usable, and the automatic configuration is retried on the next launch.
