# Gaming Live Translator

Gaming Live Translator is a Windows desktop application designed to bridge communication gaps for multilingual gaming squads and co-op teams. By providing real-time voice speech-to-text, low-latency machine translation, synthetic voice output, and an unobtrusive in-game transparent HUD overlay, players can communicate seamlessly with teammates across different languages without leaving the game.

## Current Status

**Phase 1 — project foundation and navigation only. No speech, translation, or audio functionality implemented yet.**

This release establishes the .NET 8 WPF project structure, zero-dependency MVVM navigation framework, dark gaming visual design system, metric cards on the Dashboard, skeleton views for future modules, and service contract interfaces.

## Planned Features

The application is architected to support both **cloud-based** (Deepgram streaming, Google Translate / DeepL, cloud neural TTS) and **offline/local** (Whisper local models, local neural translation, Windows SAPI / Piper TTS) providers, selectable by the user based on latency and privacy needs. This flexibility is why all core speech, translation, and audio pipelines are designed behind modular C# interfaces from the start.

Upcoming implementation phases include:
1. **Phase 2**: API configuration & secure key storage (DPAPI / Windows Credential Manager)
2. **Phase 3**: Audio capture pipeline (WASAPI loopback & microphone selection)
3. **Phase 4**: Deepgram live streaming speech-to-text integration
4. **Phase 5**: Neural translation pipeline with caching
5. **Phase 6**: Low-latency Text-to-Speech synthesis
6. **Phase 7**: Transparent in-game HUD overlay window
7. **Phase 8**: Global hotkeys & Discord voice integration

## Prerequisites

- Windows 10/11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or compatible newer .NET SDK with Windows Desktop workload)
- Visual Studio 2022 (v17.8+) or Visual Studio Code with C# Dev Kit

## How to Build and Run

### Command Line (.NET CLI)

1. Navigate to the project root directory:
   ```cmd
   cd "d:\pubgpc ch"
   ```

2. Build the solution:
   ```cmd
   dotnet build GamingLiveTranslator.sln
   ```

3. Run the WPF application:
   ```cmd
   dotnet run --project GamingLiveTranslator/GamingLiveTranslator.csproj
   ```

### Visual Studio 2022

1. Open `GamingLiveTranslator.sln` in Visual Studio 2022.
2. Ensure the build configuration is set to `Debug` | `Any CPU`.
3. Set `GamingLiveTranslator` as the startup project.
4. Press `F5` (or click **Start Debugging**) to run.
