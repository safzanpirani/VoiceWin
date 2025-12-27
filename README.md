# VoiceWin

Native Windows Speech-to-Text Transcription App

## Features

- **Hold Right Alt** to record, release to transcribe and paste
- **Tap Right Alt** to toggle recording (configurable)
- Supports **Groq Whisper API** (fast, free tier available)
- Supports **Deepgram nova-3** (high quality)
- System tray app - runs in background
- Auto-paste transcribed text to focused window

## Requirements

- Windows 10/11
- .NET 8.0 SDK

## Setup

1. Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0

2. Build the project:
```bash
cd F:\specprojects\voicewin
dotnet restore
dotnet build
```

3. Run the app:
```bash
dotnet run --project src/VoiceWin
```

4. Configure your API keys in the settings window

## API Keys

### Groq (Recommended - Fast & Free)
1. Go to https://console.groq.com
2. Create an account and get an API key
3. Paste in the "Groq API Key" field

### Deepgram
1. Go to https://console.deepgram.com
2. Create an account and get an API key
3. Paste in the "Deepgram API Key" field

## Usage

1. Press and hold **Right Alt** to start recording
2. Speak into your microphone
3. Release **Right Alt** to stop and transcribe
4. Text is automatically pasted into the focused text field

## Hotkey Modes

- **Hold to Record**: Hold the key while speaking, release to transcribe
- **Tap to Toggle**: Tap once to start, tap again to stop

## Build for Release

```bash
dotnet publish src/VoiceWin -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output will be in `src/VoiceWin/bin/Release/net8.0-windows/win-x64/publish/`
