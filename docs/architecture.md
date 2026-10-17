# Architecture Roadmap

This roadmap distinguishes implemented milestones from planned functionality.

- V0.1 — Cursor companion: complete
- V0.2 — Global controls and application state: complete
- V0.3 — Screen capture and vision: complete; verified end to end against the live provider
- V0.4 — Voice input/output: complete; local Whisper recognition and local Piper synthesis, verified end to end
- V0.5 — Visual guidance overlays: implemented; highlights drawn over located elements
- V0.6 — Windows UI Automation grounding: implemented; model targets snapped to real element bounds
- V1.0 — First complete working version: in progress; response panel, typed input, packaging, settings, tray icon, logging and privacy consent implemented

## Vision Pipeline

```text
LOCAL TRUST BOUNDARY

Screen
  ↓
ScreenCaptureService
  ↓
CapturedFrame
  ↓
VisionService
  ↓
VisionRequest

EXTERNAL TRUST BOUNDARY — entered only by explicit Ctrl + Alt + D or a spoken turn

                         IVisionProvider
                              │
                 ┌────────────┴────────────┐
                 ▼                         ▼
       OpenAIVisionProvider       GeminiVisionProvider
                 │                         │
                 ▼                         ▼
          OpenAI Responses     Gemini Interactions API
                 │                         │
                 ▼                         ▼
          gpt-5.4-mini          gemini-3.8-flash
                                           │
                                           ▼
                                      VisionResult
```

Three providers now share the boundary, which is what keeps it honest. `GeminiVisionProvider` uses raw HTTP against the Interactions API, `OpenAIVisionProvider` uses the official SDK's Responses API, and `OpenAICompatibleVisionProvider` uses raw HTTP against chat-completions with a configurable base URL. That last one is deliberately not built on the Responses API: third-party endpoints - OpenRouter, Groq, Together, Azure, and local servers such as Ollama or LM Studio - implement chat-completions and almost none implement Responses, so the choice of endpoint shape is what makes one provider reach many vendors.

Guidance parsing is shared by all three. The format is the one `VisionPromptBuilder` asks for rather than anything a vendor defines, so the parser was renamed away from implying otherwise. Only the normalised-to-pixel conversion is provider business.

Neither the OpenAI nor the compatible path has been exercised against a live endpoint; both were written without a key or a local server available, and that is recorded here rather than left to be discovered.

The provider is selected at startup by `VisionProviderFactory` from `CURSORAI_VISION_PROVIDER`, defaulting to Gemini. Selection is explicit and total: one provider is constructed, an unknown name is a startup error rather than a silent default, and nothing is ever substituted at runtime. `IVisionProvider` extends `IDisposable` so the composition root holds the interface rather than a concrete type merely to release a transport.

`VisionResult` carries the response text plus best-effort metadata: the model as reported by the provider, token usage where available, and latency. Latency is measured by `VisionService` rather than inside a provider so it means the same thing for all of them. Every metadata field is nullable, because providers differ in what they report and an absent count must not read as zero.

Gemini is the currently selected development provider. Its single Interactions API request explicitly uses `store=false`; no `previous_interaction_id`, background execution, streaming, tools, or server-side conversation state is requested. The request carries an explicit 30-second timeout rather than the 100-second .NET default.

The Interactions API wraps response payloads in a single-element array, unlike `generateContent`, which returns a bare object. Both shapes are accepted. When a payload cannot be parsed at all, the raw body is reported (redacted and truncated) instead of being replaced by a generic message — a parsing defect must never be reported as a provider fault. `OpenAIVisionProvider` sets `store=false` as well (the Responses API stores by default, which would have made the privacy disclosure untrue on that path) and the same 120-second timeout as the other providers. It remains available, demonstrating that `VisionRequest`, `VisionResult`, `VisionService`, `VisionPromptBuilder`, and capture models are provider-neutral. Each provider alone owns its transport/SDK types, model identifier, credential lookup, request translation, and response extraction.

Capture correctness rests on two explicit decisions. The application declares per-monitor DPI awareness through `app.manifest`, because the capture path treats `GetCursorPos`, `GetMonitorInfo`, and `BitBlt` output as true physical pixels; measured 2026-10-08, the process ran as system-DPI-aware without that manifest. The buddy window is also excluded from capture with `SetWindowDisplayAffinity`, since `CAPTUREBLT` otherwise includes it and it would occlude the cursor target. Multi-monitor and mixed-DPI behaviour remains unverified: the development machine has a single display.

Captures run on the UI thread only as far as the BitBlt, which keeps the cursor sample aligned with the keypress. PNG encoding and file writes move to a background thread; the captured frame is frozen, which is what makes that safe.

Transient Gemini failures (429, 500, 502, 503, 504) are retried up to three times with exponential backoff, and `Retry-After` is honoured: any wait longer than ten seconds fails immediately instead. That rule exists because the free tier allows only 20 requests per day per model and answers an exhausted quota with `Retry-After` hours ahead—retrying spent three requests to be refused three times. Retry applies to the same model only; this is not provider fallback, and no alternative model or provider is ever substituted automatically.

Guidance coordinates come from the provider in screenshot pixels. Gemini reports boxes in its own normalised 0-1000 `box_2d` convention and `GeminiVisionProvider` converts them, so no vendor coordinate format reaches the provider-neutral layer. That convention is not a preference: measured against synthetic screens with known rectangles on 2026-10-12, requesting raw screenshot pixels produced a systematic vertical scaling error, mean IoU 0.24 with centres 29 to 54 pixels out, while the normalised convention gave mean IoU 0.91 to 0.99 with centres within 2 pixels. Three coordinate spaces meet in `OverlayGeometry` — screenshot pixels, physical screen pixels, and device-independent units — and it is kept pure because a single-display machine cannot exercise the multi-monitor paths.

UI Automation refines rather than replaces the model. `UiAutomationElementSource` reads the foreground window through `AutomationElement.FromHandle`, using the handle capture already records; locating a window by enumerating the desktop's children took 1795 ms by comparison. `TargetSnapper` then matches each model box to the element with the greatest overlap and substitutes its exact bounds, keeping the model's own label because that is what the user heard in the answer. The overlap threshold matters: a nameless pane spanning an entire Electron window scores under 0.01 against a button-sized box and is correctly ignored, while a menu item scores 0.5 against a box drawn around just its label text and is correctly matched. Any window exposing nothing usable keeps the model's coordinates unchanged, so this step can only improve accuracy, never reduce it.

The guidance overlay is excluded from capture with `SetWindowDisplayAffinity`, exactly as the buddy is. Without that, highlights drawn for one question would be captured for the next and the model would describe its own guidance.

Speech recognition runs locally through Whisper (Whisper.net and Whisper.net.Runtime, both MIT, verified from package metadata). Whisper cannot stream, so the contract gained a push-to-talk predicate: `RegisterHotKey` reports presses but never releases, so the release is polled with `GetAsyncKeyState` and the utterance ends when the key comes up. Audio is captured at 16 kHz mono 16-bit PCM, which is exactly what the model consumes, held in memory, and discarded after transcription; it is never written to disk and never leaves the machine.

Models are read from `%LOCALAPPDATA%\CursorAI\models` and are never downloaded automatically. A missing model degrades to the legacy Windows recognizer with instructions naming the file, the folder and the command, because a fresh install has no model and voice input would otherwise simply be dead. The legacy engine mishears badly but exists on every Windows machine, which makes it a floor rather than a silence.

Synthesis runs locally through Piper, invoked as a persistent child process. Persistence is the design rather than an optimisation: measured on the development machine, starting Piper per answer cost 6.74s for 9.8s of speech of which only 1.55s was inference, while a process held open produced first audio after 0.65s cold and 0.45s warm. Raw PCM is streamed from stdout and played as it arrives, so playback starts well before synthesis finishes.

Piper emits no delimiter between utterances, so the end of one is inferred from the output stream going quiet. That rule needs two phases rather than one, because an idle stream means "not started" until the first chunk arrives: applying the idle gap alone made a ten-second answer report completion in 0.41s. The heuristic only decides when the call returns; it cannot truncate speech, because playback drains whatever is already buffered.

Windows.Media.SpeechSynthesis was evaluated and rejected on measurement: it exposes only David, Zira and Mark on the development machine, the same engine System.Speech already uses, with no natural voices. Kokoro-82M is Apache-2.0 for code and weights but needs ONNX Runtime plus a phonemizer in .NET, and its usual phonemizer is itself GPL-3.0, so it costs more work and reintroduces the licence question. Piper is run as a separate process whose files the user supplies, so CursorAI distributes no Piper code and no GPL code, and the instructions point at the archived MIT build rather than the maintained GPL one.

A spoken turn runs recognition, capture, analysis, and playback inside one activity, advancing Listening to Thinking to Responding without releasing ownership. Speech never crosses the external trust boundary: both engines are on-device, so the audio path adds no network surface and spends no provider quota. Engine availability depends on machine state outside CursorAI's control, so a missing recogniser, microphone, or output device disables voice alone and is reported at startup rather than preventing the application from running.

The response panel is the third window excluded from capture, alongside the buddy and the guidance overlay, and for the same reason: all three are layered because of `AllowsTransparency`, so `CAPTUREBLT` would otherwise fold CursorAI's own output into the next screenshot it takes. Unlike the other two it is deliberately not click-through, because it is typed into and scrolled. It reuses `BuddyPositioning` for placement so the panel and the buddy cannot disagree about how to handle screen edges.

`BuddyStateManager` distinguishes two kinds of writer. Ambient changes, such as the idle/listening toggle, are cosmetic and may be refused. Activities, such as a vision request and later a voice turn, take exclusive ownership through `TryBeginActivity` and restore the prior state when their scope is disposed. Conflating the two was a defect: the activation hotkey used to flip state to Idle mid-request, killing the Thinking animation while the call was still running, and the request's own cleanup then forced Idle regardless of what the user had selected.

The development **Ctrl + Alt + S** path stops inside the local boundary and saves only local PNG/JSON artifacts. The **Ctrl + Alt + D** path, and a spoken turn started with **Ctrl + Alt + A**, cross the external boundary explicitly. There is no automatic or background capture and no automatic provider fallback.

Each of the three request paths publishes its cancellation source, so one trigger can abandon whichever is in flight. That matters because a request routinely takes tens of seconds on a throttled free tier, and the only previous way to stop one was to quit. Cancelling stops waiting rather than un-sending, so the allowance may still have been spent and the user is told that. Playback is only interrupted when something is actually being spoken, since stopping Piper restarts the process and costs a model reload.

`VisionService` owns a `ConversationMemory` shared by every request path, so a typed follow-up can refer to a spoken question. Each request carries the recent turns as text, and `VisionPromptBuilder` renders them ahead of the current question; providers need no change because history arrives inside the prompt rather than as provider-specific message arrays, which keeps all three providers identical. Turns are recorded only after an answer arrives, capped at six, truncated at 1500 characters, and discarded after ten idle minutes. They are never persisted. Earlier screenshots are not retained: the current one is always attached and the prompt says to trust it over earlier answers.

Every path that sends the screen off the machine is gated on an accepted privacy disclosure, checked again before each request rather than only at startup, so a declined session can be reconsidered without restarting. Agreement is stored as a version number: changing what leaves the machine invalidates it and asks again, rather than inheriting an answer given about something else. Declining is a supported state, not an error - local-only capture keeps working - so the gate never has to choose between informed agreement and a usable application. Failing to show the disclosure fails closed.

The notification-area icon is the only persistent, discoverable surface: a click-through buddy with no taskbar entry teaches nobody that a shortcut exists. It is built on WinForms' NotifyIcon, whose implicit global usings are removed in the project file because they collide with WPF's Point, Size, Application and KeyEventArgs throughout.

Diagnostics are mirrored to a dated file under `%LOCALAPPDATA%` by wrapping the console writers, rather than by adding a logging call at every site, so the log cannot drift out of step with what the console shows. Files are pruned after seven days because they can contain window titles.

Credentials resolve from the environment first and from stored user settings second. The ordering keeps an existing development workflow behaving exactly as before while making the application usable by someone who never opens a terminal; a value set in both places is not ambiguous, and the settings window names the fields the environment is controlling. The settings window applies its edits on top of the stored settings rather than building a fresh record, so fields it does not display, the privacy agreement among them, survive a save. Stored credentials are encrypted with DPAPI at `CurrentUser` scope under `%LOCALAPPDATA%\CursorAI`, never beside the executable, because an installed build may sit somewhere unwritable. A settings file copied from another machine or account cannot be decrypted, which degrades to no credential rather than to a crash.

For development, secrets may still come from the process environment: `GEMINI_API_KEY` for the active Gemini provider and `OPENAI_API_KEY` if OpenAI is selected later. Credentials must never be hard-coded or committed. Screenshots, cursor/monitor context, foreground metadata, and the temporary question may be sensitive and must be transmitted only as an explicit, documented user action.
