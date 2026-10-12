# Architecture Roadmap

This roadmap distinguishes implemented milestones from planned functionality.

- V0.1 — Cursor companion: complete
- V0.2 — Global controls and application state: complete
- V0.3 — Screen capture and vision: complete; verified end to end against the live provider
- V0.4 — Voice input/output: complete; local-only speech recognition and synthesis, verified end to end
- V0.5 — Visual guidance overlays: planned
- V0.6 — Windows UI Automation grounding: planned
- V1.0 — First complete working version: planned

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

The provider is selected at startup by `VisionProviderFactory` from `CURSORAI_VISION_PROVIDER`, defaulting to Gemini. Selection is explicit and total: one provider is constructed, an unknown name is a startup error rather than a silent default, and nothing is ever substituted at runtime. `IVisionProvider` extends `IDisposable` so the composition root holds the interface rather than a concrete type merely to release a transport.

`VisionResult` carries the response text plus best-effort metadata: the model as reported by the provider, token usage where available, and latency. Latency is measured by `VisionService` rather than inside a provider so it means the same thing for all of them. Every metadata field is nullable, because providers differ in what they report and an absent count must not read as zero.

Gemini is the currently selected development provider. Its single Interactions API request explicitly uses `store=false`; no `previous_interaction_id`, background execution, streaming, tools, or server-side conversation state is requested. The request carries an explicit 30-second timeout rather than the 100-second .NET default.

The Interactions API wraps response payloads in a single-element array, unlike `generateContent`, which returns a bare object. Both shapes are accepted. When a payload cannot be parsed at all, the raw body is reported (redacted and truncated) instead of being replaced by a generic message — a parsing defect must never be reported as a provider fault. `OpenAIVisionProvider` remains available, demonstrating that `VisionRequest`, `VisionResult`, `VisionService`, `VisionPromptBuilder`, and capture models are provider-neutral. Each provider alone owns its transport/SDK types, model identifier, credential lookup, request translation, and response extraction.

Capture correctness rests on two explicit decisions. The application declares per-monitor DPI awareness through `app.manifest`, because the capture path treats `GetCursorPos`, `GetMonitorInfo`, and `BitBlt` output as true physical pixels; measured 2026-10-08, the process ran as system-DPI-aware without that manifest. The buddy window is also excluded from capture with `SetWindowDisplayAffinity`, since `CAPTUREBLT` otherwise includes it and it would occlude the cursor target. Multi-monitor and mixed-DPI behaviour remains unverified: the development machine has a single display.

Captures run on the UI thread only as far as the BitBlt, which keeps the cursor sample aligned with the keypress. PNG encoding and file writes move to a background thread; the captured frame is frozen, which is what makes that safe.

Transient Gemini failures (429, 500, 502, 503, 504) are retried up to three times with exponential backoff, and `Retry-After` is honoured: any wait longer than ten seconds fails immediately instead. That rule exists because the free tier allows only 20 requests per day per model and answers an exhausted quota with `Retry-After` hours ahead—retrying spent three requests to be refused three times. Retry applies to the same model only; this is not provider fallback, and no alternative model or provider is ever substituted automatically.

A spoken turn runs recognition, capture, analysis, and playback inside one activity, advancing Listening to Thinking to Responding without releasing ownership. Speech never crosses the external trust boundary: both engines are on-device, so the audio path adds no network surface and spends no provider quota. Engine availability depends on machine state outside CursorAI's control, so a missing recogniser, microphone, or output device disables voice alone and is reported at startup rather than preventing the application from running.

`BuddyStateManager` distinguishes two kinds of writer. Ambient changes, such as the idle/listening toggle, are cosmetic and may be refused. Activities, such as a vision request and later a voice turn, take exclusive ownership through `TryBeginActivity` and restore the prior state when their scope is disposed. Conflating the two was a defect: the activation hotkey used to flip state to Idle mid-request, killing the Thinking animation while the call was still running, and the request's own cleanup then forced Idle regardless of what the user had selected.

The development **Ctrl + Alt + S** path stops inside the local boundary and saves only local PNG/JSON artifacts. The **Ctrl + Alt + D** path, and a spoken turn started with **Ctrl + Alt + A**, cross the external boundary explicitly. There is no automatic or background capture and no automatic provider fallback.

Secrets come from the process environment: `GEMINI_API_KEY` for the active Gemini provider and `OPENAI_API_KEY` if OpenAI is selected later. Credentials must never be hard-coded or committed. Screenshots, cursor/monitor context, foreground metadata, and the temporary question may be sensitive and must be transmitted only as an explicit, documented user action.
