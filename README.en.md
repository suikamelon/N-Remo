# N-Remo

[日本語](README.md) | English

N-Remo is an unofficial WPF application for controlling Nature Remo, SwitchBot, and Tuya devices from Windows. It provides freely configurable buttons, cloud commands, sensor displays, logs, and simple automations in one application.

> [!IMPORTANT]
> N-Remo is not an official application of Nature Inc., SWITCHBOT Inc., or Tuya Inc. All service names, product names, and trademarks belong to their respective owners.

## Features

- Capture and transmit infrared signals through the Nature Remo local API
- Execute commands through the Nature Remo Cloud API, SwitchBot API, and Tuya API
- Freely add, remove, and arrange control buttons
- Edit button labels, colors, sizes, stacking order, and visual effects
- Display sensor data from Nature Remo, SwitchBot, and Tuya
- Sensor logging with interactive time-range charts
- Simple automation with multiple conditions and multiple commands
- SwitchBot Webhook integration through Cloudflare WebSocket or Firebase Realtime Database with SSE
- Global hotkeys, mouse gestures, and system tray support
- Back up and restore settings and button data
- Japanese and English user interfaces

## System Requirements

- Windows 10 version 2004 (build 19041) or later
- x64 environment
- Internet connection when using cloud APIs or Webhook integration
- Same LAN connection when using the Nature Remo local API

The self-contained distribution does not require a separate .NET Desktop Runtime installation.

## Installation

1. Download the latest distribution ZIP from [Releases](https://github.com/suikamelon/N-Remo/releases).
2. Extract the ZIP to a folder of your choice.
3. Run `N-Remo.exe`.

If the distribution includes the `Language` or `WebhookTemplates` folders, keep them together with `N-Remo.exe`.

Windows SmartScreen may appear because the distributed executable is not code-signed. Verify the download source and the SHA-256 value published in the Release notes.

## Initial Setup

Right-click the main window, open **Settings**, and configure only the services you use.

### Nature Remo

- A cloud access token is not required when using only the local API.
- An access token is required for server commands and the cloud API.

### SwitchBot

- Enter the Token and Secret obtained from the SwitchBot app.
- For Webhook integration, follow the instructions in `WebhookTemplates`.

### Tuya

- Enter the Access ID, Access Secret, and regional endpoint created in the Tuya IoT Platform.

> [!WARNING]
> Settings backups contain credentials such as Tokens, Secrets, and passwords. Do not share them with others, and store them in a secure location.

## Webhook Templates

- `WebhookTemplates/Cloudflare`: Cloudflare Workers, Durable Objects, and WebSocket
- `WebhookTemplates/Firebase`: Google Apps Script, Firebase Realtime Database, and SSE

Each folder contains a detailed beginner-oriented setup guide in `README.txt`.

## Building from Source

Requirements:

- Visual Studio 2026 Community or later, or the .NET 10 SDK
- .NET desktop development workload
- Windows 10 SDK 10.0.19041.0 or later

```powershell
dotnet restore N-Remo.csproj
dotnet build N-Remo.csproj -c Release
```

Example self-contained publish command:

```powershell
dotnet publish N-Remo.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Data Storage

Settings, button data, and logs are stored in the user's environment. Do not commit real user data, `MyData.db`, sensor logs, backup files, Tokens, or Secrets to the repository.

## Bugs and Feature Requests

Please use [GitHub Issues](https://github.com/suikamelon/N-Remo/issues). Do not post logs or screenshots containing Tokens, Secrets, Webhook URLs, passwords, personal information, or other credentials.

For security issues, follow [SECURITY.en.md](SECURITY.en.md).

For privacy information, see [PRIVACY.en.md](PRIVACY.en.md).

## License

N-Remo is released under the [MIT License](LICENSE).

Copyright (c) 2026 suikamelon
