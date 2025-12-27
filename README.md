# VoiceWin

Native Windows Speech-to-Text Transcription App

## Features

- **Hold Right Alt** to record, release to transcribe and paste
- **Tap Right Alt** to toggle recording (configurable)
- **Hybrid mode** - hold for quick recordings, tap to toggle for longer ones
- **Real-time Streaming** - see transcriptions appear as you speak (Deepgram)
- **AI Enhancement** - polish transcriptions with LLM-powered rewriting
- **Sound Feedback** - audio cues when recording starts/stops
- **Tray Icon Status** - visual indicator shows recording (red), processing (orange), or ready (green)
- Supports **Groq Whisper API** (fast, free tier available)
- Supports **Deepgram nova-3** (high quality, batch or streaming)
- System tray app - runs in background
- Auto-paste transcribed text to focused window
- Single portable EXE - no installation required

## Requirements

- Windows 10/11 x64

## Quick Start

Download `VoiceWin.exe` from [Releases](https://github.com/safzanpirani/voicewin/releases) and run it - no installation needed.

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

### Non-Streaming (Groq, Deepgram)
Records audio, then transcribes after you stop. Fast and reliable.

### Streaming Mode (Deepgram Streaming)
Transcribes in real-time as you speak - text appears immediately. Best used with **Toggle** hotkey mode.

> **Note:** When starting streaming mode, there's a ~1 second delay while connecting to the Deepgram WebSocket. The "recording started" sound plays once the connection is live and transcription is ready - so wait for the sound before speaking.

> **Note:** Streaming mode adds a trailing space after each transcript chunk to separate words. Some applications (especially terminals, code editors, and certain text fields) may strip trailing whitespace from clipboard paste. If words are running together, try:
> - Using **non-streaming mode** instead (more reliable for these apps)
> - Adjusting your terminal/editor settings to preserve trailing spaces
> - Using a different target application

## AI Enhancement

Enable AI Enhancement to automatically polish your transcriptions using Groq's LLM API:

- Fixes grammar, spelling, and punctuation
- Removes filler words and stutters
- Improves sentence structure and clarity
- Preserves your original meaning and tone

Customize the enhancement prompt in settings to match your preferred output style.

## Tray Icon

The system tray icon shows the current status:
- 🟢 **Green** - Ready
- 🔴 **Red** - Recording
- 🟠 **Orange** - Processing/Transcribing

## Settings

Settings are stored at:
```
%APPDATA%\VoiceWin\settings.json
```

## Build from Source

Requires .NET 8.0 SDK.

```bash
# Development build
dotnet restore
dotnet build src/VoiceWin -c Release

# Self-contained single-file EXE (no .NET required to run)
dotnet publish src/VoiceWin -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src/VoiceWin/bin/Release/net8.0-windows/win-x64/publish/VoiceWin.exe` (~156MB)

## License

MIT
