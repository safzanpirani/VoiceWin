# AGENTS.md - VoiceWin

Guidelines for AI agents working on this WPF speech-to-text application.

## Build Commands

```bash
# Restore + Build (Debug)
dotnet restore && dotnet build src/VoiceWin -c Debug

# Release build
dotnet build src/VoiceWin -c Release

# Self-contained single-file EXE (~156MB, no .NET runtime required)
dotnet publish src/VoiceWin -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

**No test framework configured.** Validate changes manually:
1. Build: `dotnet build src/VoiceWin -c Debug`
2. Run: `src/VoiceWin/bin/Debug/net8.0-windows/VoiceWin.exe`
3. Configure API keys, test recording with Right Alt key

## Project Structure

```
src/VoiceWin/
├── Assets/           # EmbeddedResource (icon, sounds)
├── Models/           # Data classes (AppSettings, TranscriptionResult)
├── Services/         # Business logic (one service per concern)
├── Views/            # WPF windows (MainWindow.xaml + code-behind)
├── App.xaml          # Application entry point
└── VoiceWin.csproj   # .NET 8.0 Windows WPF project
```

## Code Style

### Naming Conventions
| Type | Convention | Example |
|------|------------|---------|
| Classes, methods, properties, events | PascalCase | `TranscriptionOrchestrator` |
| Local variables, parameters | camelCase | `audioData` |
| Private fields | _camelCase | `_isProcessing` |
| Async methods | Async suffix | `TranscribeAsync` |

### File Organization
- One class per file (except private nested DTOs)
- Filename = classname, namespace = folder path (`VoiceWin.Services`)

### Imports (using statements)
```csharp
// Order: System → Third-party → Project
using System.Net.WebSockets;
using System.Text.Json;
using VoiceWin.Models;
```
Implicit usings enabled—only add explicit when needed.

### Types & Nullability
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Use `?` for nullable types
- **NEVER** use `!` null-forgiving operator without justification
- **NEVER** use `dynamic` or `object` when concrete types are known

### Error Handling
- Wrap external API calls in try-catch
- Emit errors via `ErrorOccurred` event—don't swallow silently
- Empty catch blocks acceptable ONLY for fire-and-forget audio playback

## Architecture Patterns

### Service Pattern (Single Responsibility)
| Service | Responsibility |
|---------|----------------|
| `AudioRecordingService` | Microphone capture |
| `GroqTranscriptionService` | Groq Whisper API |
| `DeepgramTranscriptionService` | Deepgram batch API |
| `DeepgramStreamingService` | Deepgram WebSocket streaming |
| `TextPasteService` | Clipboard operations |
| `SoundService` | Audio feedback |
| `SettingsService` | Persistence to %APPDATA% |
| `GlobalHotkeyService` | Win32 hotkey registration |

### Orchestrator Pattern
`TranscriptionOrchestrator` wires services together. All cross-service coordination happens here.

### Event-Driven Communication
```csharp
public event EventHandler<string>? TranscriptReceived;
public event EventHandler<string>? ErrorOccurred;
public event EventHandler? RecordingStarted;
```

## Critical Implementation Details

### 1. Clipboard = STA Thread Required
```csharp
var thread = new Thread(WorkerMethod);
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
```

### 2. Streaming Paste Order
Use `BlockingCollection<string>` with single consumer thread:
```csharp
private readonly BlockingCollection<string> _pasteQueue = new();
foreach (var text in _pasteQueue.GetConsumingEnumerable()) { ... }
```

### 3. Embedded Resources (Single-File EXE)
Assets must be `EmbeddedResource`, not `Content`:
```xml
<EmbeddedResource Include="Assets\sound_start.mp3" />
```
Load with:
```csharp
Assembly.GetExecutingAssembly().GetManifestResourceStream("VoiceWin.Assets.sound_start.mp3");
```

### 4. NAudio Seekable Stream
Copy embedded resource to MemoryStream first:
```csharp
using var memoryStream = new MemoryStream();
stream.CopyTo(memoryStream);
memoryStream.Position = 0;
using var audioFile = new Mp3FileReader(memoryStream);
```

### 5. WebSocket Best Practices
- Set `KeepAliveInterval` for long connections
- Use `CancellationTokenSource` with timeout
- Send `{"type":"CloseStream"}` before closing
- Handle `OperationCanceledException` and `WebSocketException` explicitly

### 6. UI Thread Access
```csharp
Dispatcher.Invoke(() => { /* UI update */ });
```

## Settings

Stored at: `%APPDATA%\VoiceWin\settings.json`

| Setting | Values |
|---------|--------|
| `TranscriptionProvider` | `"groq"`, `"deepgram"`, `"deepgram-streaming"` |
| `HotkeyMode` | `"hold"`, `"toggle"`, `"hybrid"` |
| `HotkeyVirtualKey` | `165` (Right Alt default) |
| `PlaySoundFeedback` | `true`/`false` |

## Dependencies

| Package | Purpose |
|---------|---------|
| NAudio | Audio recording/playback |
| Deepgram | SDK types (raw WebSocket for streaming) |
| Hardcodet.NotifyIcon.Wpf | System tray icon |
| InputSimulatorPlus | Keyboard simulation (Ctrl+V) |

## Common Pitfalls

1. **Clipboard in background threads** → Use Win32 API directly, not WPF Clipboard
2. **Clipboard unavailable** → Retry with backoff (10 attempts, 30ms delay)
3. **WebSocket blocking** → Always use async with cancellation tokens
4. **Undisposed services** → Implement IDisposable, unsubscribe events
5. **Assets in single-file** → Use EmbeddedResource, not Content
6. **Streaming paste sync** → Queue and process on STA thread

## Hotkey Mode Notes

| Mode | Best For | Known Issues |
|------|----------|--------------|
| Hold | Batch transcription | Streaming has Alt+Ctrl+V conflicts |
| Toggle | Streaming transcription | — |
| Hybrid | Mixed usage | Quick tap can't stop with another tap |

## Version Info

- .NET 8.0 Windows (WPF)
- Target: Windows 10/11 x64
