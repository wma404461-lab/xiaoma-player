# Xiaoma Player + Recorder

A Windows media viewer, image-sequence player and screen recorder. Source version: 0.6.23. Includes a native WebView2 host and the existing Electron development entry point. Supports video, images, sequences, document/model previews and selected-area recording.

## Development

Install Node.js 22+ and dependencies with `npm install` or `pnpm install`. Windows builds also require the .NET Framework 4.8 development environment.

```powershell
npm test
npm run build
npm run start:legacy
```

The independent source copy passed eight core regressions and the frontend/C# helper build. The build includes the missing UpdateResources.cs input for UpdateHelper. These checks reused installed dependencies and do not certify every device, recording path or fresh-machine setup.

## Native packaging

`npm run package` uses `scripts/package-native.cjs`. Prepare the documented `.runtime/native-sdk` layout with WebView2 SDK, BouncyCastle, ScreenRecorderLib, NAudio, 7-Zip and the required packaging tools. Adjust the Roslyn compiler path to the installed .NET SDK version. The native installer was not rebuilt in this source publication.

By default, packaging stages the result, backs up the local `dist` application and preserves its `.data` folder. Set `PLAYER_PACKAGE_DIR` to a separate output directory to avoid in-place installation. This source repository does not include signed application binaries.

## Usage and licensing

See [User guide](USER_GUIDE.md) and [Recording guide](docs/RECORDING_GUIDE.txt). Icon regeneration uses sharp. Project-authored code is MIT licensed; JavaScript dependencies, FFmpeg, WebView2 and recording/audio libraries retain their own licenses. Packaging copies dependency notices; verify the complete licenses for the chosen binary versions before redistribution.

Private keys, deployment scripts, personal media and recordings are excluded. Public update configuration and verification keys remain; publisher signing keys are not included. Configure your own update endpoint before distributing a fork.
