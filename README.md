# CursorAI

CursorAI is a Windows-native AI desktop companion being built incrementally.

Current status: **V0.3.3 — First Real Screen Understanding**

Technology stack: C#, .NET 10, WPF

V0.1 Cursor Companion and V0.2 Controls & State are complete. V0.3.3 now includes two implementations of the provider-neutral vision boundary:

- `GeminiVisionProvider`, currently selected for development and using `gemini-3.8-flash` through the Gemini Interactions API
- `OpenAIVisionProvider`, retained for future selection and using `gpt-5.4-mini`

There is no provider-selection UI and no automatic fallback. The active provider is chosen at startup from `CURSORAI_VISION_PROVIDER`:

```powershell
$env:CURSORAI_VISION_PROVIDER = "openai"   # or "gemini" (the default)
```

Exactly one provider is constructed. An unrecognised value stops startup with an explanatory message rather than quietly defaulting, because a typo must never result in screenshots going to a different vendor than intended.

## Development shortcuts

- **Ctrl + Alt + Space** toggles the existing Idle and Listening states. Microphone listening is not implemented.
- **Ctrl + Alt + Shift + S** performs a local-only capture of the monitor containing the cursor and writes a PNG plus JSON context beneath `artifacts/captures/`. It sends nothing externally.
- **Ctrl + Alt + Shift + V** explicitly captures the monitor containing the cursor and sends the screenshot, cursor/monitor context, foreground process/title, and temporary question to the currently selected provider. Gemini is selected for development.

The temporary vision question is: “What am I looking at, and what is under my cursor?” The returned text is currently written to the terminal used to launch CursorAI with `dotnet run`, together with the model, latency, and token counts the provider reported; no chat or response UI exists yet.

While a request is in flight it owns the buddy's visual state: the idle/listening toggle is refused rather than cancelling the Thinking animation, and when the request ends the state that was current beforehand is restored.

If another application already owns one of these shortcuts, CursorAI starts anyway and lists the unavailable ones on stderr; only losing all three is fatal. On the development machine **Ctrl + Alt + Space** is owned by another process, which previously crashed CursorAI at startup.

## API keys

The active Gemini provider requires `GEMINI_API_KEY`. Set it for the current PowerShell process/session before launching CursorAI:

```powershell
$env:GEMINI_API_KEY = "<your-api-key>"
dotnet run --project .\src\CursorAI.App\CursorAI.App.csproj
```

Replace the placeholder locally. Never hard-code or commit the real key.

`GEMINI_MODEL` is optional and overrides the default model without a rebuild:

```powershell
$env:GEMINI_MODEL = "gemini-3.5-flash"
```

The default is `gemini-3.8-flash`. Transient capacity failures are retried up to three times. Do not select `gemini-3.6-flash`, which retires 2026-11-19.

### Free-tier quota

The Gemini free tier allows **20 requests per day, per model**. Each **Ctrl + Alt + Shift + V** press spends one. When the daily allowance is gone the API answers 429 with `Retry-After` set hours ahead; CursorAI reports that and stops rather than retrying, because retrying would spend further requests to receive the same refusal.

Because the quota is per model, `GEMINI_MODEL` is also the way to keep testing after exhausting one. Measured on 2026-10-08: a full-resolution 1920x1080 call to `gemini-3.5-flash` completed in 4.4 seconds; `gemini-3.8-flash` under heavy load exceeded 30 seconds, which is why the request timeout is 60 seconds.

`OPENAI_API_KEY` is needed only if application composition is changed to select `OpenAIVisionProvider`:

```powershell
$env:OPENAI_API_KEY = "<your-api-key>"
```

These assignments affect only the current PowerShell process/session and child processes started from it.

## Privacy boundary

The buddy window is excluded from screen capture via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`. Before this, the window—layered because of `AllowsTransparency`—was pulled into CursorAI's own screenshots by `CAPTUREBLT`, covering an 80x80 pixel region at cursor+(20,20) and occluding the very thing the cursor pointed at.

Screenshots and foreground window titles can contain sensitive or private information. No automatic capture, background monitoring, periodic screenshots, or screen recording occurs. The local **S** shortcut never uploads data. Only an explicit **Ctrl + Alt + Shift + V** activation sends the current monitor screenshot and selected context to the active provider—currently Google’s Gemini Interactions API. CursorAI explicitly sends `store=false`, so this V0.3.3 request is stateless and is not retained as an Interaction object for later retrieval or continuation. This setting does not make broader claims about Google’s processing or data-use practices.

Developers using Gemini’s free tier should avoid testing with sensitive or private screens and review Google’s current Gemini API data-use terms before submitting real sensitive information.
