# Third-party notices

T16Bridge uses and redistributes the following required third-party components.

## HIDMaestro

- Project: https://github.com/hifihedgehog/HIDMaestro
- Pinned release: `v1.9.0`
- License: MIT
- Purpose: creates the virtual `T16LEFT` / `T16RIGHT` HID devices.

The exact upstream license text is downloaded during the release build and is included in the installer under `licenses/HIDMaestro-LICENSE.txt`.

## HidHide

- Project: https://github.com/nefarius/HidHide
- Pinned signed release: `v1.5.230.0`
- License: MIT
- Purpose: hides the original physical T.16000M devices from games while allowing T16Bridge to read them.

The exact upstream license text is downloaded during the release build and is included in the installer under `licenses/HidHide-LICENSE.txt`.

## HidSharp

- Project/package: `HidSharp` NuGet package
- Version: `2.6.4`
- Purpose: reads the two physical T.16000M HID input reports.

Its NuGet package is resolved by `dotnet restore` and bundled into the self-contained application publish output according to its package license terms.
