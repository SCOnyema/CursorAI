# CursorAI — Full Project Brief

**Snapshot date:** 2026-10-12
**Purpose:** Self-contained context for a collaborating Claude chat with no repository access.
**Author of this brief:** Claude Code (Opus 5), after a full read of the codebase, a live API probe, and a documentation check.

> **How to read this document.** Every factual claim is tagged:
> `[VERIFIED]` — I ran it, probed it, or read it directly and observed the result.
> `[READ]` — I read the code and the conclusion follows directly from it.
> `[REASONED]` — high confidence from mechanism, but not empirically confirmed.
> `[UNKNOWN]` — genuinely open; do not assume either way.
>
> Please preserve these tags when reasoning about the project. The single biggest failure mode in this project's history was a confident-sounding claim that turned out to be unverified.

---

## 0. Status - 2026-10-12

**V0.1 through V0.4 are complete and verified end to end.** Build clean (0 warnings),
**91/91 tests pass**, no test touches the network. Working tree clean at `2a5b831`.

`[VERIFIED]` **The whole product loop works.** Press the Ask shortcut, speak a question,
and CursorAI recognises it on-device, captures the monitor containing the cursor, asks the
vision provider, prints the answer with model/latency/token metadata, and reads it aloud.
Confirmed by the project owner on 2026-10-12.

`[VERIFIED]` **Cursor grounding works at word resolution on real screens.** Live calls
located the cursor over a specific word ("around the letters e or d") and over a particular
phrase inside a sidebar, correctly placing it above a Log in button.

### Commits this far

```
2a5b831 feat: add voice turns and configurable hotkeys
dc59cb9 feat: implement vision provider selection and metadata reporting
68e27a5 fix: make V0.3.3 screen understanding work end to end
9b75f26 feat: add vision provider architecture
5d84aea feat: add capture and cursor grounding
70ec039 feat: add monitor screen capture foundation
533d18a refactor: harden CursorAI lifecycle and cleanup
629ab5c feat: add global hotkey and visual feedback
0ec40f4 feat: add CursorAI application state model
```

### Shortcuts (changed 2026-10-12)

| Action | Shortcut | Mnemonic |
|---|---|---|
| Spoken turn | `Ctrl + Alt + A` | **A**sk |
| Local-only capture | `Ctrl + Alt + S` | **S**creenshot |
| Vision, canned question | `Ctrl + Alt + D` | **D**escribe |

Three adjacent home-row keys rather than four-key chords. All three are overridable via
`CURSORAI_HOTKEY_ACTIVATE` / `_CAPTURE` / `_VISION`.

### Everything resolved since this brief was first written

| Was | Now |
|---|---|
| Array-wrapped error envelope discarded every Gemini error | `[VERIFIED]` fixed; real messages surface |
| `error.code` typed `int?`, threw on capacity errors | `[VERIFIED]` fixed; read as text (int or string) |
| `gemini-3.6-flash` retiring 2026-11-19 | `[VERIFIED]` on `gemini-3.8-flash`; `GEMINI_MODEL` override |
| 100-second .NET default timeout | `[VERIFIED]` explicit 60s, calibrated from measurement |
| 503s broke ~2 of 3 calls | `[VERIFIED]` retried 3x w/ backoff |
| Retrying an exhausted daily quota burned 3 requests | `[VERIFIED]` `Retry-After` honoured; fails fast past 10s |
| Buddy appeared in its own screenshots | `[VERIFIED]` 2371 -> **0** pixels |
| DPI awareness undeclared | `[VERIFIED]` SYSTEM_AWARE -> PER_MONITOR via manifest |
| Encode + file I/O blocked the UI thread | `[VERIFIED]` moved off-thread; BitBlt kept on it deliberately |
| One hotkey conflict crashed the app at startup | `[VERIFIED]` degrades per-hotkey |
| `Ctrl + Alt + Space` unavailable, and voice needed it | `[VERIFIED]` defaults moved; hotkeys configurable |
| Provider choice required a recompile | `[VERIFIED]` `CURSORAI_VISION_PROVIDER` |
| `IVisionProvider` did not express lifetime | `[VERIFIED]` extends `IDisposable`; App holds the interface |
| `VisionResult` discarded model/usage/latency | `[VERIFIED]` all three carried; latency owned by `VisionService` |
| Any writer could stomp the state mid-request | `[VERIFIED]` activity ownership; 5 tests pin it |
| No voice at all | `[VERIFIED]` local on-device STT + TTS |
| `Api-Revision` header requirement unknown | `[VERIFIED]` not required |
| Free tier limited to Flash-Lite? | `[VERIFIED]` no - all 50 models visible; 20 req/day/model |

### Facts worth not rediscovering

- **Free tier: 20 requests per day, per model.** Each Describe press or spoken turn spends
  one. Quota is per model, so `GEMINI_MODEL` is how to keep working after exhausting one.
- **Real-world full-resolution latency is ~25s**, not the 4.4s a synthetic image suggests;
  real screens are far denser. This is why the timeout is 60s.
- **Display is 120 DPI (125% scaling)**, so a 64x64 DIP window is 80x80 physical pixels.
- **`RegisterHotKey` is a system-wide first-come claim** and Windows exposes no way to ask
  who holds a combination. Measured: only `Ctrl + Alt + Space` and `Alt + F4` were owned out
  of 51 tried; all 26 `Ctrl + Alt + <letter>` were free.
- **Speech engines present on this machine:** 5 voices, en-US recogniser `MS-1033-80-DESK`.

### Still open

1. `[UNKNOWN]` **Multi-monitor and mixed-DPI.** Single-display machine; `CursorGrounding`'s
   negative-origin math has never executed. Matters more at V0.5, since an overlay has to
   land on the right monitor.
2. ~~No way to quit.~~ **RESOLVED 2026-10-12.** `Ctrl + Alt + Q` shuts CursorAI down, and a
   single-instance mutex makes a second launch exit with an explanation instead of competing
   for the same hotkeys. `[VERIFIED]`: second launch exits code 2; a synthesized Ctrl + Alt + Q
   ended the first instance cleanly with code 0. A tray icon is deferred to V1.0 polish - it
   needs Shell_NotifyIcon, a dedicated message-only window (the buddy is WS_EX_NOACTIVATE and
   click-through, so TrackPopupMenu misbehaves on it), an authored icon and menu P/Invoke,
   which is a lot of surface for a problem a hotkey already solves.
3. **GDI BitBlt is the wrong primitive for V0.5.** Returns black for hardware-overlay and
   protected content, `CAPTUREBLT` can flicker, whole-monitor only.
   `Windows.Graphics.Capture` gives per-window capture, which V0.6 UI Automation wants too.
4. `DevelopmentCaptureStorage.Save` takes `Environment.CurrentDirectory` through a parameter
   named `repositoryRoot`; three `artifacts/` directories exist as a result.
5. `DevelopmentCaptureStorageTests` still covers only `CreateFileName`, which nothing in
   production calls, while `Save` itself has no coverage.
6. `[UNKNOWN]` Does `CAPTUREBLT` cause visible flicker on this hardware?
7. Open design question: the canned Describe question remains hard-coded. Voice supersedes
   it, so decide whether Describe keeps a fixed prompt, gains free-text input, or is retired.

---

## 1. What CursorAI is

CursorAI is a **Windows-native AI desktop companion**. A small always-on-top "buddy" follows the mouse cursor, can *see* the screen on demand, and — when complete — will listen to voice, answer aloud, and draw visual guidance on top of real UI elements to walk the user through tasks.

It is **not** an editor plugin and not a chat window. The defining idea is **cursor grounding**: the question it answers is not "what is on screen" but *"what is the user pointing at, right now."* Every capture carries the cursor's exact pixel coordinates, and the prompt explicitly tells the model that the cursor may not be rendered in the image and to reason from the supplied coordinates instead.

The intended end state: *you speak to it, it looks at your screen, understands what is under your cursor, answers you aloud, and points at what you should click.*

### Project history

Originally scaffolded by **OpenAI Codex** across six commits. Development stalled for roughly a week when the Gemini vision integration would not work and produced an unusable error message (see §6 — this is now diagnosed). The project is being **handed from Codex to Claude Code** as of 2026-10-08.

---

## 2. Stack and layout

- **Language/runtime:** C#, .NET 10 (`net10.0-windows`), WPF, `Nullable` and `ImplicitUsings` enabled
- **Platform:** Windows 11 Pro (dev machine: 10.0.26200)
- **Tests:** xUnit 2.9.3 + coverlet, `InternalsVisibleTo("CursorAI.Tests")`
- **NuGet:** `Google.GenAI` 1.21.1, `OpenAI` 2.14.0
- **Repo:** git, branch `main`, author Samuel Onyema

```
CursorAI.sln
README.md
docs/architecture.md
docs/project-brief.md          <- this file
src/CursorAI.App/
  App.xaml(.cs)                 composition root; constructs GeminiVisionProvider
  MainWindow.xaml(.cs)          the buddy window + all three hotkey handlers
  BuddyPositioning.cs           pure: cursor -> clamped window position
  NativeMethods.cs              all P/Invoke (user32, kernel32, gdi32)
  DevelopmentConsole.cs         AttachConsole to parent for dev output
  InternalsVisibleTo.cs
  Models/BuddyState.cs          enum: Idle, Listening, Thinking, Responding, Guiding
  Input/HotkeyParser.cs         pure: "Ctrl + Alt + A" -> modifiers + virtual key
  Input/HotkeyDefinition.cs / HotkeyConfiguration.cs   configured shortcuts
  Speech/ISpeechRecognizer.cs / ISpeechSynthesizer.cs  local-only speech boundary
  Speech/WindowsSpeechRecognizer.cs / WindowsSpeechSynthesizer.cs
  Speech/LocalSpeechServices.cs    creates engines; absence degrades, never throws
  Speech/SpokenResponseFormatter.cs  strips markdown before playback
  State/IBuddyActivity.cs       exclusive state ownership for one activity
  State/BuddyStateManager.cs    CurrentState + StateChanged + activity ownership
  State/BuddyStateChangedEventArgs.cs
  Capture/
    ScreenCaptureService.cs     GDI BitBlt of the monitor containing the cursor
    CapturedFrame.cs            frozen BitmapSource + all context
    CapturedMonitorInfo.cs      bounds, work area, isPrimary
    CursorGrounding.cs          pure: screen coords -> image coords
    ForegroundWindowInfo.cs     hwnd, title, process name
    PhysicalScreenBounds.cs / PhysicalScreenPoint.cs
    DevelopmentCaptureStorage.cs    writes PNG + JSON sidecar
    DevelopmentCaptureArtifacts.cs  returned paths
  Vision/
    IVisionProvider.cs          AnalyzeAsync(VisionRequest, CancellationToken)
    VisionRequest.cs            provider-neutral: PNG bytes + all screen context
    VisionResult.cs             currently: response text ONLY
    VisionService.cs            encodes PNG, builds request, delegates to provider
    VisionPromptBuilder.cs      builds the grounded text prompt
    GeminiVisionProvider.cs     raw HttpClient, Interactions API  <- ACTIVE
    OpenAIVisionProvider.cs     official OpenAI SDK, Responses API <- inactive
    GeminiApiErrorDiagnostics.cs  error formatting + secret redaction
tests/CursorAI.Tests/
  BuddyPositioningTests.cs (3)  BuddyStateManagerTests.cs (4)
  CursorGroundingTests.cs (4+)  DevelopmentCaptureStorageTests.cs (1)
  VisionServiceTests.cs (2)     GeminiVisionProviderTests.cs (7)
  VisionPromptBuilderTests.cs (2)
artifacts/                     gitignored; local capture output
```

---

## 3. Roadmap

| Version | Capability | Status |
|---|---|---|
| V0.1 | Cursor companion (floating buddy) | complete |
| V0.2 | Global hotkeys + application state model | complete |
| V0.3 | Screen capture + vision | **complete**, verified end to end |
| V0.4 | Voice input/output | **complete**, verified end to end |
| V0.5 | Visual guidance overlays | next |
| V0.6 | Windows UI Automation grounding | planned |
| V1.0 | First complete working version | planned |

The `BuddyState` enum already contains `Responding` and `Guiding` with finished XAML animations, anticipating V0.4 and V0.5. `[VERIFIED]` — a grep confirms nothing ever sets either state; only `Idle`, `Listening`, and `Thinking` are reachable today.

### Commit history

```
9b75f26 feat: add vision provider architecture
5d84aea feat: add capture and cursor grounding
70ec039 feat: add monitor screen capture foundation
533d18a refactor: harden CursorAI lifecycle and cleanup
629ab5c feat: add global hotkey and visual feedback
0ec40f4 feat: add CursorAI application state model
```

One roadmap step per commit, with README and `docs/architecture.md` updated in the same commit. **The V0.3.3 work is currently uncommitted** — 5 new source files, 2 new test files, and edits to README, architecture doc, `App.xaml.cs`, `MainWindow.xaml.cs`, `NativeMethods.cs`, `HotkeyService.cs`, and the csproj.

---

## 4. What actually works today

`[VERIFIED]` Build: `dotnet build` succeeds, **0 warnings, 0 errors**. Tests: **91/91 pass**, none touching the network.

**The buddy** — 64×64 transparent, borderless, topmost, click-through window (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`), drawn as a purple eyeball with two white eyes. Repositions every 16 ms to cursor + (16,16), DPI-converted via `TransformFromDevice`, clamped to the work area of whichever monitor holds the cursor. No taskbar entry, no tray icon.

**`Ctrl+Alt+Space`** — toggles Idle ⇄ Listening; a cyan ring pulses. Purely cosmetic: **no microphone exists.**

**`Ctrl+Alt+Shift+S`** — local-only capture. GDI BitBlt of the monitor containing the cursor → PNG + JSON sidecar under `artifacts/captures/`, plus a console dump of monitor bounds, work area, primary flag, cursor position (screen *and* image coords), foreground process/title, and image size. **Sends nothing externally.**

**`Ctrl+Alt+Shift+V`** — capture *and* analyze. Captures, sets `Thinking` (spinning dashed ring), builds the grounded prompt, POSTs base64 PNG + context to Gemini with `store=false`, prints elapsed seconds and the response text, returns to `Idle`. Guards re-entry while a call is in flight; cancels on shutdown.

The hard-coded development question is:
> `"What am I looking at, and what is under my cursor?"`

**Output channel** — `Console.WriteLine` only. `DevelopmentConsole.TryAttachToParent()` calls `AttachConsole(ATTACH_PARENT_PROCESS)`, so output appears **only** when launched from a terminal. Launched from Explorer or a debugger, all output silently vanishes. There is **no chat or response UI** of any kind.

### How to run

```powershell
$env:GEMINI_API_KEY = "<key>"
dotnet run --project .\src\CursorAI.App\CursorAI.App.csproj
```

`[VERIFIED]` `GEMINI_API_KEY` is **not currently set** in the environment. There is no way to quit from the UI — close the terminal or use Task Manager.

---

## 5. Design principles worth preserving

These are genuinely good and should survive the handoff.

**1. The privacy trust boundary is architecture, not documentation.** `docs/architecture.md` draws an explicit local-vs-external boundary. The `S` path terminates inside `DevelopmentCaptureStorage` and *physically cannot* reach the network; only `V` crosses outward. No background monitoring, no periodic capture, no screen recording, no automatic provider fallback. `store=false` is sent explicitly and asserted in a test.

**2. Provider neutrality is proven, not asserted.** `[READ]` Two implementations use maximally different transports — `GeminiVisionProvider` is raw `HttpClient` with hand-rolled JSON records; `OpenAIVisionProvider` uses the official SDK's `ResponsesClient`. **Zero** vendor types leak into `VisionRequest`, `VisionResult`, `VisionService`, or `VisionPromptBuilder`. This is the strongest evidence the abstraction is real, and it is the best thing in the codebase.

**3. Cursor grounding as the core concept** — including `VisionPromptBuilder.cs:23` warning the model the cursor may not be rendered.

**4. Secret hygiene well above typical.** Env-var only, never hard-coded; re-read per call so a key can be set without restarting; `GeminiApiErrorDiagnostics` redacts the key by value, redacts `api_key:`-style patterns via regex, strips URLs, strips base64 runs ≥80 chars, and caps messages at 1000 chars.

**5. Win32 resource discipline in the spots people get wrong.** `ScreenCaptureService.cs:145-147` reselects the previous GDI object *before* deleting the bitmap (correct order). `NativeMethods.cs:172-188` calls `Marshal.SetLastPInvokeError(0)` before `GetWindowLongPtr` to distinguish "returned 0" from "failed." `HotkeyService` rolls back partial hotkey registration on failure.

**6. Testability by construction.** All pure logic is extracted into static, injectable units; `GeminiVisionProvider` accepts an injectable `HttpClient`.

**7. Documentation that states what is *absent*.** The README explicitly says "microphone listening is not implemented," "no provider-selection UI," "no automatic fallback." This discipline should continue.

---

## 6. ⚠️ THE BLOCKER — diagnosed and FIXED

> **STATUS (2026-10-08): fixed and verified end-to-end against the live API.**
> `ParseApiError` and `ParseResponseText` now parse via `JsonDocument` and accept both the
> array-wrapped and bare-object envelopes. An unparseable body is reported verbatim
> (redacted, truncated) instead of being replaced by a generic sentence. Six regression
> tests added, one using the real captured body verbatim.
>
> `[VERIFIED]` A live call with a deliberately invalid key now produces:
> ```
> Gemini API request failed.
> Failure type: Gemini API error response
> HTTP status: 400 (BadRequest)
> Gemini API code: 400
> Gemini API status: INVALID_ARGUMENT
> API message: API key not valid. Please pass a valid API key.
> ```
> Previously this same call reported only `API message: Gemini returned a malformed error response.`
>
> The history below is retained because it is the reason for the `[VERIFIED]`/`[UNKNOWN]` discipline.

**This section explains the week-long stall.**

The user chose Gemini over OpenAI to avoid spending money during V0.3 development. The integration never worked and produced an error message with no actionable information, so work stopped.

### Codex's integration is almost entirely correct

`[VERIFIED]` I probed the live API with a dummy key:

| Request | Result |
|---|---|
| `POST /v1beta/interactions` | **HTTP 400** `API_KEY_INVALID` |
| `POST /v1beta/models/gemini-2.5-flash:generateContent` | **HTTP 400** `API_KEY_INVALID` |
| `POST /v1beta/thispathdoesnotexist` (control) | **HTTP 404** |

The control returning 404 while `/v1beta/interactions` returns an auth error proves **the endpoint exists**. Codex did not hallucinate it. The Gemini Interactions API is real and went GA around June 2026.

`[VERIFIED]` Against the official docs, Codex's request shape is correct:

```json
{
  "model": "gemini-3.6-flash",
  "input": [
    { "type": "image", "data": "<base64>", "mime_type": "image/png" },
    { "type": "text",  "text": "<prompt>" }
  ],
  "store": false
}
```

And the response parsing path is correct: `steps[]` → `type == "model_output"` → `content[]` → `type == "text"` → `.text`.

### The actual defect

`[VERIFIED]` The Interactions endpoint returns errors **array-wrapped**:

```json
[{ "error": { "code": 400, "message": "...", "status": "INVALID_ARGUMENT" } }]
```

whereas `generateContent` returns a bare `{"error":{...}}`. Codex wrote `ParseApiError` for the **bare-object** shape:

```csharp
// GeminiVisionProvider.cs ~line 169
return JsonSerializer.Deserialize<GeminiApiErrorEnvelope>(responseJson, JsonOptions)?.Error
    ?? new GeminiApiError(null, null, null);
```

`[VERIFIED]` I compiled Codex's exact `GeminiApiErrorEnvelope` / `GeminiApiError` record types and fed them the real response body captured above:

```
THREW JsonException -> provider falls back to its generic string
   (The JSON value could not be converted to GeminiApiErrorEnvelope)
```

The `catch (JsonException)` at `GeminiVisionProvider.cs:177` swallows it and substitutes `"Gemini returned a malformed error response."` with `code = null` and `status = null`. So whatever Gemini was actually reporting — invalid key, wrong model, quota exceeded, payload too large — the terminal showed:

```
Gemini API request failed.
Failure type: Gemini API error response
HTTP status: 4xx (BadRequest)
API message: Gemini returned a malformed error response.
```

**The diagnostics layer built to aid debugging destroyed the only piece of information needed, and blamed Google for malformed JSON that was in fact perfectly well-formed.** The HTTP status survives (it comes from the response object, not the JSON body), but the message does not.

`[UNKNOWN]` **Which underlying error was actually hit.** That information was discarded before it ever reached the terminal. Fix the parser, make one call, and it will say.

### Fix

Parse with `JsonDocument` and handle both shapes (if the root is an array, take element 0, then read `error`). Roughly a one-line change plus a regression test using the real array-wrapped body as a fixture.

---

## 7. External clocks

`[VERIFIED]` **`gemini-3.6-flash` retires 2026-11-19.** ✅ **RESOLVED 2026-10-08** — the active model is now `gemini-3.8-flash`, the successor Google names. The Interactions quickstart uses `gemini-3.8-flash`, `gemini-3.8-pro`, and `gemini-3.1-flash-image`. The model ID remains a single `const` in `GeminiVisionProvider`.

`[UNKNOWN]` **Free-tier changes dated 2026-10-09.** Multiple outlets report Google restricting free users to Flash-Lite. **Important caveat:** that reporting concerns the **consumer Gemini app** tiers (AI Plus at $4.99, AI Pro, AI Ultra), and I could **not** confirm it applies to the **AI Studio API free tier**. The official rate-limits page defers to AI Studio for actual numbers. Action: check real limits at `aistudio.google.com/rate-limit` before relying on free capacity. Do not treat "free tier is being cut" as established fact for the API.

`[UNKNOWN]` **The `Api-Revision` header.** The official Interactions quickstart curl example sends `-H "Api-Revision: 2026-05-20"`. Codex's code does **not** send it. The Interactions overview page makes no mention of the header, so whether it is required, optional, or merely a pinning convenience is unresolved — as is whether omitting it changes the response envelope shape (which would be interesting, given §6). Worth testing once a real key is available.

---

## 8. Review findings, prioritized

### 1. The buddy appears in its own screenshots - `[VERIFIED]`, now FIXED
> **Confirmed then fixed 2026-10-08.** All four captures carrying metadata held 1600-2371
> `#6C63FF` pixels in an 80x80 box at cursor+(20,20). The geometry independently confirmed
> 125% display scaling: the 80x80 box at +20 captured 95% of matches versus 65% for a 64x64
> box at +16. After adding `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, a fresh
> CAPTUREBLT of the buddy's own window rect found **0** body pixels out of 6400.
> The vision model had been shown the buddy instead of whatever it covered.
`MainWindow.xaml:9` sets `AllowsTransparency="True"`, which makes the WPF window `WS_EX_LAYERED`. `NativeMethods.cs:167` deliberately ORs in `CAPTUREBLT` (`0x40000000`), whose documented purpose is to **include layered windows** in the result. The buddy is 64×64 at cursor + (16,16) — so it occludes exactly the region the prompt asks the model to interpret, and the model may describe the eyeball itself. A self-referential feedback loop.
**Fix:** `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` at window init (Win10 2004+) — excludes the window from capture without hiding it or racing the compositor.
**Confirm in one keypress:** press `Ctrl+Alt+Shift+S` and look at the PNG.

### 2. DPI awareness is load-bearing but never declared - `[VERIFIED]`, now DECLARED
> **Measured and fixed 2026-10-08.** The default .NET manifest declared no DPI setting at
> all; the process ran as `PROCESS_SYSTEM_DPI_AWARE` (awareness 1) on a 120 DPI / 125%
> display. Added `app.manifest` requesting PerMonitorV2 -> now `PROCESS_PER_MONITOR_AWARE`.
> **Known discrepancy:** V2 is requested but V1 resolves (awareness 2, not 3). Removing the
> legacy `dpiAware` element does not change it, so the downgrade comes from the WPF host.
> V1 satisfies the only property CursorAI depends on - unvirtualised physical coordinates.
> **Multi-monitor remains unverified.**

Original finding: `[VERIFIED]` There is no `app.manifest`, no `<ApplicationHighDpiMode>`, and nothing DPI-related in the generated `runtimeconfig.json`. The entire capture path assumes `GetCursorPos`, `GetMonitorInfo`, and `BitBlt` return true physical pixels — which holds only for a per-monitor-DPI-aware process. That currently depends on framework defaults rather than an explicit decision.
`[VERIFIED]` Compounding it: **every** capture JSON in `artifacts/` shows a single 1920×1080 primary monitor at origin (0,0), with `cursor.screen` identical to `cursor.relativeToCapture`. So the careful multi-monitor negative-origin math in `CursorGrounding` — which `CursorGroundingTests` covers in theory — **has never executed in practice.**
**Fix:** declare PerMonitorV2 explicitly; test on a second monitor at a different scale factor *before* V0.5 overlays depend on these coordinates.

### 3. GDI BitBlt is the wrong long-term capture primitive
`[REASONED]` It returns black for hardware-overlay and protected content (DRM video, exclusive-fullscreen games, windows with `WDA_MONITOR` affinity); `CAPTUREBLT` causes visible screen flicker on some configurations; and it is a synchronous full-monitor blit. `Windows.Graphics.Capture` (`GraphicsCaptureItem` + `Direct3D11CaptureFramePool`) is the supported modern path and offers **per-window** capture — which is what V0.6 UI Automation grounding will want anyway. Cheaper to swap before overlays depend on it.

### 4. Capture, PNG encode, and file I/O all run on the UI thread - now FIXED
> **Fixed 2026-10-08.** Encoding and both file writes moved to `Task.Run`. The BitBlt and
> cursor sample deliberately stay on the UI thread: `ScreenCaptureService` reads
> `GetCursorPos` internally, so hopping threads first would sample the cursor later than the
> keypress and drift the one coordinate this product is grounded on.

Original finding: `[READ]` `MainWindow.xaml.cs:107-108` performs BitBlt → PNG encode → two file writes inline in the hotkey handler; line 156 does the BitBlt inline too. All of it competes with the 16 ms cursor-tracking `DispatcherTimer`. On a 4K monitor this is a visible hitch. `ScreenCaptureService.cs:140` already calls `image.Freeze()`, so handing the frame to a background thread is nearly free.

### 5. No state-transition validation; the state machine desyncs from reality
`[READ]` `BuddyStateManager.SetState` permits **any** transition from any state. Press `Ctrl+Alt+Space` during a vision call and `MainWindow.xaml.cs:92-94` sees `CurrentState != Idle` and flips to `Idle`, killing the `Thinking` animation while the HTTP request is still in flight. Then the `finally` at line 204 **unconditionally** forces `Idle`, stomping whatever the user chose. V0.4 voice adds a second concurrent activity and this gets materially worse.
**Fix:** validate legal transitions, and/or ignore the activation hotkey while a request is running; save/restore rather than hard-set `Idle`.

### 6. `IVisionProvider` does not express lifetime; the composition root leaks
`[READ]` `App.xaml.cs:9` declares `private readonly GeminiVisionProvider _visionProvider` — the **concrete** type — purely so `OnExit` can call `Dispose()`. That defeats the abstraction at the one place it is load-bearing. Separately, `OpenAIVisionProvider` holds a `ResponsesClient` it **never** disposes, with non-thread-safe lazy init at line 44.
**Fix:** `IVisionProvider : IDisposable` (or a provider factory).

### 7. No explicit HTTP timeout
`[READ]` `new HttpClient()` means the .NET default of 100 seconds. A hung request pins `_isVisionAnalysisRunning = true` and spins the buddy for a full 100 s with no UI cancel. Ironic, given how much care `FormatCancellation` takes to *report* the timeout value. Set an intentional ~30 s.

### 8. `Save(frame, Environment.CurrentDirectory)` with a parameter named `repositoryRoot`
`[VERIFIED]` The mismatch is already observable: **three** `artifacts/` directories exist in the tree — repo root, `src/CursorAI.App/`, and `tests/CursorAI.Tests/`. The README's claim that captures land "beneath `artifacts/captures/`" is true only when launched from the repo root. Tests also write into the source tree (`tests/CursorAI.Tests/artifacts/verification`) instead of a temp directory.
**Fix:** resolve a stable location (`AppContext.BaseDirectory` or `%LOCALAPPDATA%`), or rename the parameter and document the behavior.

### 9. `DevelopmentCaptureStorageTests` covers the one method nothing uses
`[VERIFIED]` `CreateFileName` is called **only** by that test; production goes `Save` → `CreateBaseFileName`. So a dead method has 100% coverage while `Save`'s real behavior — directory creation, the `FileMode.CreateNew` same-millisecond `IOException`, the JSON schema — has none. The single decorative test in an otherwise-strong suite.

### 10. NEW - a single hotkey conflict was a fatal startup crash - `[VERIFIED]`, now FIXED
> **Found while trying to run the app 2026-10-08.** `Ctrl + Alt + Space` is owned by another
> process on this machine, so `RegisterHotKey` failed, `HotkeyService` threw, and the app
> died with an unhandled `Win32Exception` - over the one hotkey with no functional behaviour
> behind it. Probing all three: Space **TAKEN**, Shift+S and Shift+V available. This very
> likely contributed to the project feeling broken.
> Registration now degrades per-hotkey and lists unavailable shortcuts on stderr; only
> losing all three throws.

### Smaller items
- `NativeMethods.cs:132` `TryGetWindowTitle` calls `GetWindowTextLength` then `GetWindowText`; a title that grows between the two silently truncates.
- `TryGetCursorMonitorContext` emits an all-zero rect on failure. Correct today because every caller checks the bool, but a zero-sized rect is a footgun; a nullable return would express it better.
- `GeminiApiErrorDiagnostics` `CredentialPattern` replacement `"$1<redacted>"` drops the separator, yielding `key<redacted>`. Cosmetic.
- `SanitizeMessage` strips **all** URLs. Defensible against secrets-in-query, but since auth is in a header this mostly destroys debugging signal.
- No way to quit the app — click-through, no taskbar entry, no tray icon. Add a tray icon before anyone else uses it.
- Console output vanishes silently outside a terminal (see §4).
- `VisionResult` carries **only** text, discarding model id, token usage, and latency — all of which both APIs return and V1.0 will want. The Interactions response includes `id`, `model`, `status`, `usage.total_tokens`, `created`, `updated`.

### Overall assessment

The engineering judgment is better than typical agent-generated scaffolding — the trust boundary, secret redaction, and GDI unwind order all required actually caring. The weakness is concentrated in one place: **everything that cannot be unit-tested on a single 1080p monitor is unverified.** The capture primitive, the DPI assumption, the multi-monitor math, and the live API call all carry the project's weight and none have met reality. Findings 1, 2, and 3 should be settled before V0.4, because V0.5 overlays will inherit whatever coordinate model is locked in now.

---

## 9. Plan

Phases A (unblock), B (capture fidelity), C (abstraction) and D's first step (V0.4 voice)
are all complete. What remains:

### Before V0.5 - decide the capture primitive
Overlays inherit whatever coordinate model is locked in, so swapping GDI BitBlt for
`Windows.Graphics.Capture` afterwards means redoing them. Recommended: migrate first.
Per-window capture is also what V0.6 UI Automation grounding wants.

### V0.5 - visual guidance overlays
Draw on top of real UI elements to point at what the user should click. Depends on
coordinates being trustworthy, which is why items 1 and 3 under "Still open" matter here
rather than later.

### V0.6 - Windows UI Automation grounding
Replace pixel reasoning with real element identity where available.

### Housekeeping, any time
A tray icon with Quit (item 2 - it has broken the build twice); the capture-directory
parameter (item 4); real coverage for `DevelopmentCaptureStorage.Save` (item 5).

## 10. Open questions

1. RESOLVED - most likely the startup hotkey crash and/or a 503, both of which rendered as
   "malformed error response". The original text is unrecoverable, but the class of fault is
   identified and every such error is legible going forward.
2. RESOLVED - `Api-Revision` is **not** required. Plain requests 503 at the same rate.
3. RESOLVED for the API - the key lists all 50 models including `gemini-3.8-flash`. The
   reported restriction applies to the consumer Gemini app, not AI Studio API keys.
4. RESOLVED - it was `PROCESS_SYSTEM_DPI_AWARE`; now per-monitor via manifest. See the
   V1-vs-V2 discrepancy in finding 2.
5. RESOLVED - yes, confirmed with pixel evidence, then fixed.
6. `[UNKNOWN]` Does `CAPTUREBLT` cause visible flicker on this hardware?
7. `[UNKNOWN]` **Multi-monitor / mixed-DPI correctness.** Needs a second display.
8. Open design question: what replaces the single hard-coded dev question once there is a
   real UI - free-text input, voice, or a small set of intents?

---

## 11. Working arrangement

**Claude Code (in the repo)** does the hands-on work: reading and editing files, running builds and tests, probing APIs, verifying against reality. It has the repository, a shell, and network access.

**This Claude chat** is a design and reasoning partner — architecture trade-offs, API semantics, roadmap sequencing, reviewing proposed approaches, and acting as a second opinion on findings. It receives progress reports.

**What this chat cannot do:** read the repository, run the build, execute tests, or call the Gemini API. Anything requiring ground truth has to be requested from Claude Code and reported back.

**The one standing request:** keep the `[VERIFIED] / [READ] / [REASONED] / [UNKNOWN]` discipline. This project lost a week to a confidently-worded error message that was false — the provider reported "Gemini returned a malformed error response" when Gemini's response was perfectly well-formed and the bug was local. Distinguishing what is *known* from what is *assumed* is not pedantry here; it is the lesson the project already paid for.

### Immediate dependency

A **Gemini API key** must be present in the shell session that launches the app (`$env:GEMINI_API_KEY`). It is not currently set. Code can be read, endpoints probed, and schemas checked without it — but the successful vision call, which is the project's entire unproven seam, cannot be verified until a key exists.
