# Gaming Live Translator — Known Limitations & Architectural Decisions

This document consolidates key architectural decisions, licensing obligations, third-party service constraints, and intentional scope deferrals for the **Gaming Live Translator** project. It serves as a persistent technical reference for maintainers and developers.

---

## 1. Licensing & Attribution

### Offline Translation: Argos Translate vs. LibreTranslate
- **Why Argos Translate was chosen**: LibreTranslate is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**, which contains a strict network-use copyleft clause requiring any networked application interacting with or wrapping it to be licensed under AGPL-3.0. Argos Translate is licensed under **MIT / CC0**, which is fully permissive and allows embedding without copyleft obligations.
- **Packaging Footprint**: The bundled Argos runtime omits PyTorch and SpaCy, utilizing **MiniSBD** sentence boundary detection and **CTranslate2 / ONNXRuntime** to keep the disk footprint under ~125 MB instead of >3 GB.

### Offline TTS: Piper-plus vs. Standard Piper Forks
- **Why Piper-plus was chosen**: Official Piper builds and most upstream forks depend on **espeak-ng**, which is licensed under **GPL-3.0**, introducing copyleft compliance requirements for distributed binaries. Piper-plus replaces espeak-ng with a custom G2P (Grapheme-to-Phoneme) engine and MB-iSTFT-VITS2 architecture, maintaining clean **MIT** licensing across the runtime.

### Tsukuyomi-chan Multilingual Voice Model Attribution
- **Model**: `tsukuyomi-chan-6lang-fp16.onnx` (Multilingual 6-language model: Japanese, Chinese, English, Spanish, French, Portuguese).
- **Corpus License**: Governed by the **Tsukuyomi-chan Corpus Terms of Use**, which require explicit attribution:
  - **Character Name**: つくよみちゃん (Tsukuyomi-chan)
  - **Creator**: 夢前黎 (Rei Yumesaki)
  - **Official Resource URL**: [https://tyc.rei-yumesaki.net/](https://tyc.rei-yumesaki.net/)
- **In-App Attribution**: Displayed in the Settings UI under the Piper Offline Voice Packages card.

### Edge TTS (`rany2/edge-tts`)
- **License**: **LGPLv3** (GNU Lesser General Public License v3.0), with `srt_composer.py` under MIT.
- **Compliance Architecture**: `edge-tts` is executed as an isolated subservice inside the bundled local Python runtime (`tts_server.py`) and invoked via loopback HTTP. Under LGPLv3 §4, this constitutes a dynamic Combined Work / Application interface, ensuring the C# host application remains unencumbered.
- **Bundled License**: The full LGPLv3 text is distributed at `Resources/PythonRuntime/EDGE_TTS_LICENSE.txt` and deployed to the application bin directory.

### Ambiguity in Upstream Voice Model Repositories (`rhasspy/piper-voices`)
- While the top-level GitHub / Hugging Face repository for `rhasspy/piper-voices` is tagged as MIT, individual model checkpoints are trained on disparate public datasets (e.g., LibriTTS, VCTK, M-AILABS, Thorsten). Some underlying datasets carry non-commercial or CC-BY restrictions. When adding new Piper models, developers must vet the specific training corpus license, not rely solely on the top-level repository tag.

---

## 2. Reliability & Third-Party Service Risk

### Edge TTS (Unofficial Microsoft Read Aloud Integration)
- **Protocol**: Reverse-engineers Microsoft Edge's internal browser Read Aloud WebSocket (`speech.platform.bing.com/consumer/speech/synthesize/readaloud`).
- **Risk Profile**: This is **not an official, supported Microsoft Cognitive Services API**. Microsoft may modify headers, revoke client tokens, throttle traffic, or decommission the endpoint without advance notice.
- **UI Treatment**: Explicitly labeled in the UI as **`Edge TTS (Free, Online, Unofficial)`** with a standing amber warning banner reminding users that it is an online service (unlike Piper) and best-effort.

### Multi-Account Provider Stacking
- The Translation Provider Pool (`TranslationPoolService`) is designed to coordinate failover across **genuinely distinct providers** (Google Cloud, RapidAPI Lecto, and Offline Argos).
- Configuring multiple free-tier accounts of the same service (e.g., multiple Lecto API keys) to circumvent monthly quota limits violates RapidAPI/Lecto Terms of Service. The pool does not validate account ownership and assumes keys represent legitimate, separate subscriptions.

### Exclusive Fullscreen Game Overlay Limitations
- The Subtitle HUD Overlay (`OverlayWindow.xaml`) utilizes standard Windows topmost layering (`Topmost = "True"`, `WS_EX_TOPMOST`, `WS_EX_TRANSPARENT`).
- Games running in **DirectX Exclusive Fullscreen** bypass the Windows Desktop Window Manager (DWM) composition pipeline to minimize input latency. As a result, no standard desktop window can render over an exclusive fullscreen game.
- **Required Game Setting**: Users must configure their games to **Borderless Windowed (Windowed Fullscreen)** or **Windowed Mode** for subtitles to remain visible.

### VB-CABLE & Virtual Audio Routing
- **Driver Requirement**: Routing TTS voice output into Discord or in-game voice chat relies on third-party virtual audio cables (e.g., VB-CABLE by VB-Audio).
- **Manual Setup**: Because VB-CABLE is closed-source freeware requiring administrative installation of a kernel-mode audio driver and a system reboot, the app cannot bundle or silently install it.
- **Application Routing**: The app cannot programmatically change Discord's or the game's input device setting. Users must manually set their game/Discord voice input to the virtual device.

---

## 3. Quality & Accuracy Caveats

### Argos Translate Pivot Routing
- Argos Translate models operate as unidirectional or English-centric language packages. Non-English translation pairs (e.g., Hindi to Chinese or Russian to Japanese) execute as a two-stage pivot translation:
  $$\text{Source Language} \xrightarrow{\text{Argos}} \text{English} \xrightarrow{\text{Argos}} \text{Target Language}$$
- Pivot routing incurs compound semantic degradation and grammatical nuance loss compared to direct multilingual neural translation (e.g., Google Translate).

### Tsukuyomi-chan Cross-Lingual Accent
- The bundled Piper voice model (`tsukuyomi-chan-6lang-fp16.onnx`) is fundamentally a Japanese female voice persona trained cross-lingually to synthesize other languages.
- While Mandarin Chinese and Spanish words are phonetically clear, speech cadence exhibits an audible anime-style / Japanese-accented cadence rather than native broadcast quality. For native Mandarin quality, Edge TTS or cloud providers are recommended.

### Estimated vs. Authoritative Quota Counters
- Character usage counters in the Translation Provider Pool are tracked locally in `%LOCALAPPDATA%\GamingLiveTranslator\settings.json`.
- If an API key is used concurrently in external applications or scripts, local counters will not reflect external usage. The pool relies on HTTP 429/403 status responses from the provider for hard quota enforcement.

---

## 4. Security & Privacy Architecture

### Credential Protection (Windows DPAPI)
- All third-party API keys (Deepgram, Google Cloud Translate, RapidAPI Lecto) are encrypted using the **Windows Data Protection API (`ProtectedData.Protect`)** scoped to the current Windows user (`DataProtectionScope.CurrentUser`).
- Credentials are encrypted on disk at `%LOCALAPPDATA%\GamingLiveTranslator\secrets.dat`. They are never written to plain JSON settings files, never printed to console logs, and never transmitted to any telemetry endpoint.

### Anti-Cheat Compliant Push-to-Talk
- `GlobalHotkeyService` intentionally avoids installing Windows global keyboard hooks (`WH_KEYBOARD_LL` / `SetWindowsHookEx`). Low-level hooks are frequently flagged or blocked by kernel-level anti-cheat systems (Easy Anti-Cheat, BattlEye, Riot Vanguard).
- Push-to-Talk polling uses a 35 ms high-resolution timer calling Win32 `GetAsyncKeyState`, ensuring zero injection, zero DLL hooking, and zero anti-cheat penalties.

### Localhost Loopback Isolation
- The bundled Python microservices (`translate_server.py` on port 58100, `tts_server.py` on dynamic loopback ports) bind strictly to `127.0.0.1`.
- Processes are bound to a Win32 **ChildProcessJob** object, guaranteeing child process termination when the main application closes, preventing orphaned background listeners.

---

## 5. Deliberate Scope Exclusions & Deferred Items

1. **Discord API / Bot Integration**:
   - *Decision*: Deliberately excluded. Implementing a Discord Bot would require users to set up a Discord Developer application, manage bot tokens, and invite a bot to their server. Virtual Audio Cable routing achieves the same gameplay objective (speaking translated audio into voice chat) with zero cloud setup and universal game compatibility.
2. **Independent Per-Destination Volume Control**:
   - *Decision*: Output to Local Speakers and the Virtual Audio Cable shares a unified master TTS volume slider. Independent destination mixers were deferred to prevent UI clutter and ensure consistent audio normalization across endpoints.
3. **Dynamic SSML Syntax Generation in Edge TTS**:
   - *Decision*: Only standard plain text with rate/pitch parameters is passed to Edge TTS. Custom SSML wrappers were excluded because Microsoft's Read Aloud servers actively reject non-browser SSML patterns.