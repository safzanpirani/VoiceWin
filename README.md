# VoiceWin

Native Windows Speech-to-Text Transcription App

## Features

- **Hold Right Alt** to record, release to transcribe and paste
- **Tap Right Alt** to toggle recording (configurable)
- **Hybrid mode** - hold for quick recordings, tap to toggle for longer ones
- **Real-time Streaming** - see transcriptions appear as you speak (Deepgram)
- **AI Enhancement** - polish transcriptions with LLM-powered rewriting
- Supports **Groq Whisper API** (fast, free tier available)
- Supports **Deepgram nova-3** (high quality, batch or streaming)
- System tray app - runs in background
- Auto-paste transcribed text to focused window

## Requirements

- Windows 10/11
- .NET 8.0 SDK (for building only)

## Quick Start (Pre-built)

Download `VoiceWin.exe` from Releases and run it - no installation needed.

## Setup (From Source)

1. Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0

2. Build the project:
```bash
dotnet restore
dotnet build src/VoiceWin -c Release
```

3. Run the app:
```bash
src\VoiceWin\bin\Release\net8.0-windows\VoiceWin.exe
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
- **Hybrid (Hold or Tap)**: Best of both - hold for quick recordings (≥250ms), or tap to toggle for longer sessions

## Transcription Providers

### Batch Mode (Groq, Deepgram)
Records audio, then transcribes after you stop. Fast and reliable.

### Streaming Mode (Deepgram Streaming)
Transcribes in real-time as you speak - text appears immediately. Best used with **Toggle** hotkey mode. Note: some applications may strip trailing spaces between transcript chunks.

## AI Enhancement

Enable AI Enhancement to automatically polish your transcriptions using Groq's LLM API. The feature:

- Fixes grammar, spelling, and punctuation
- Removes filler words and stutters
- Improves sentence structure and clarity
- Preserves your original meaning and tone

Customize the enhancement prompt in settings to match your preferred output style.

## Settings Location

Settings are stored at:
```
%APPDATA%\VoiceWin\settings.json
```

## Build for Release

```bash
dotnet publish src/VoiceWin -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output will be in `src/VoiceWin/bin/Release/net8.0-windows/win-x64/publish/`

This creates a single portable EXE (~156MB) that runs on any Windows 10/11 x64 machine without requiring .NET installation.
