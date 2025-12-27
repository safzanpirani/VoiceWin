# AGENTS.md - VoiceWin

Guidelines for AI agents working on this WPF speech-to-text application.

## Build Commands

```bash
# Restore dependencies
dotnet restore

# Development build
dotnet build src/VoiceWin -c Debug

# Release build
dotnet build src/VoiceWin -c Release

# Self-contained single-file EXE (no .NET runtime required)
dotnet publish src/VoiceWin -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# Output: src/VoiceWin/bin/Release/net8.0-windows/win-x64/publish/VoiceWin.exe (~156MB)
```

No test framework configured. Validate changes by building and running manually.

## Project Structure

```
src/VoiceWin/
├── Assets/           # Embedded resources (icon, sounds)
├── Models/           # Data classes (AppSettings, TranscriptionResult)
├── Services/         # Business logic (one service per concern)
├── Views/            # WPF windows (MainWindow.xaml + code-behind)
├── App.xaml          # Application entry point
└── VoiceWin.csproj   # Project file
```

## Code Style

### Naming Conventions
- **PascalCase**: Classes, methods, properties, events
- **camelCase**: Local variables, parameters
- **_camelCase**: Private fields with underscore prefix
- **Async suffix**: All async methods (`TranscribeAsync`, `ConnectAsync`)

### File Organization
- One class per file (except private nested classes for DTOs)
- File name matches class name
- Namespace matches folder structure: `VoiceWin.Services`, `VoiceWin.Models`

### Imports (using statements)
- System namespaces first, then third-party, then project namespaces
- Use implicit usings (enabled in .csproj)
- Only add explicit usings when needed

### Types
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Use `?` for nullable types, never suppress with `!` without good reason
- Avoid `dynamic` and `object` when concrete types are known
- NEVER use `as any`, `@ts-ignore`, or type suppressions

### Error Handling
- Wrap external API calls in try-catch
- Log/emit errors via events, don't swallow silently
- Use `ErrorOccurred` event pattern for async services
- Empty catch blocks `catch { }` acceptable ONLY for fire-and-forget audio playback

## Architecture Patterns

### Service Pattern
Each service has a single responsibility:
- `AudioRecordingService` - Microphone capture
- `GroqTranscriptionService` - Groq Whisper API
- `DeepgramTranscriptionService` - Deepgram batch API
- `DeepgramStreamingService` - Deepgram WebSocket streaming
- `TextPasteService` - Clipboard operations
- `SoundService` - Audio feedback
- `SettingsService` - Persistence to %APPDATA%
- `GlobalHotkeyService` - Win32 hotkey registration

### Orchestrator Pattern
`TranscriptionOrchestrator` wires services together. All cross-service coordination happens here.

### Event-Driven Communication
Services communicate via events, not direct calls:
```csharp
public event EventHandler<string>? TranscriptReceived;
public event EventHandler<string>? ErrorOccurred;
public event EventHandler? RecordingStarted;
```

## Critical Implementation Details

### 1. Clipboard Operations MUST Use STA Thread
Windows clipboard requires STA (Single-Threaded Apartment). Always use dedicated STA thread:
```csharp
var thread = new Thread(WorkerMethod);
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
```

### 2. Streaming Paste Ordering
Use `BlockingCollection<string>` with single consumer thread to ensure paste order:
```csharp
private readonly BlockingCollection<string> _pasteQueue = new();
// Consumer thread processes sequentially
foreach (var text in _pasteQueue.GetConsumingEnumerable()) { ... }
```

### 3. Trailing Spaces for Streaming
When pasting streaming transcripts, append trailing space for word separation:
```csharp
var finalText = text.EndsWith(" ") ? text : text + " ";
```
Note: Some terminals strip trailing whitespace from clipboard.

### 4. Hotkey Mode Compatibility
- **Hold mode**: Works with batch transcription. Streaming has ALT+Ctrl+V conflicts.
- **Toggle mode**: Best for streaming transcription.
- **Hybrid mode**: Known bug - quick tap starts but can't stop with another tap.

### 5. Embedded Resources for Single-File EXE
Assets must be `EmbeddedResource` (not `Content`) for single-file publish:
```xml
<EmbeddedResource Include="Assets\sound_start.mp3" />
```
Load with:
```csharp
var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VoiceWin.Assets.sound_start.mp3");
```

### 6. NAudio Stream Requirements
NAudio's `Mp3FileReader` requires seekable stream. Copy embedded resource to MemoryStream first:
```csharp
using var memoryStream = new MemoryStream();
stream.CopyTo(memoryStream);
memoryStream.Position = 0;
using var audioFile = new Mp3FileReader(memoryStream);
```

### 7. WebSocket Best Practices
- Set `KeepAliveInterval` for long connections
- Use `CancellationTokenSource` with timeout for connection
- Send `CloseStream` message before closing WebSocket
- Handle `OperationCanceledException` and `WebSocketException` explicitly

### 8. UI Thread Access
Use `Dispatcher.Invoke` or `Dispatcher.BeginInvoke` for UI updates from background threads.

## Settings

Stored at: `%APPDATA%\VoiceWin\settings.json`

Key settings:
- `TranscriptionProvider`: "groq", "deepgram", or "deepgram-streaming"
- `HotkeyMode`: "hold", "toggle", or "hybrid"
- `HotkeyVirtualKey`: 165 = Right Alt (default)
- `PlaySoundFeedback`: true/false

## Dependencies

- **NAudio** - Audio recording and playback
- **Deepgram** - SDK (used only for types, we use raw WebSocket for streaming)
- **Hardcodet.NotifyIcon.Wpf** - System tray icon
- **InputSimulatorPlus** - Keyboard simulation for paste (Ctrl+V)

## Common Pitfalls

1. **Don't use WPF Clipboard class in background threads** - Use Win32 API directly
2. **Don't assume clipboard is available** - Retry with backoff (10 attempts, 30ms delay)
3. **Don't block on WebSocket operations** - Always use async with cancellation tokens
4. **Don't forget to dispose services** - Implement IDisposable, unsubscribe events
5. **Don't use Content for assets in single-file builds** - Use EmbeddedResource
6. **Don't call PasteText synchronously in streaming** - Queue and process on STA thread

## Testing Changes

1. Build: `dotnet build src/VoiceWin -c Debug`
2. Run the EXE from `bin/Debug/net8.0-windows/`
3. Configure API keys in settings window
4. Test recording with Right Alt key
5. Verify text pastes into target application

## Version Info

- .NET 8.0 Windows (WPF)
- Target: Windows 10/11 x64
- Project started: 2025
