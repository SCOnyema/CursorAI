# CursorAI

CursorAI is a Windows-native AI desktop companion being built incrementally.

Current status: **V0.5 — Visual Guidance Overlays**

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

- **Ctrl + Alt + A** (*Ask*) starts a spoken turn: CursorAI listens, captures the monitor containing the cursor, asks the active vision provider your spoken question, then reads the answer aloud. Where speech recognition is unavailable it falls back to toggling the Idle and Listening states.
- **Ctrl + Alt + S** (*Screenshot*) performs a local-only capture of the monitor containing the cursor and writes a PNG plus JSON context beneath `artifacts/captures/`. It sends nothing externally.
- **Ctrl + Alt + Q** (*Quit*) shuts CursorAI down. The buddy is click-through with no taskbar entry, so this is the only way to stop it from the keyboard.
- **Ctrl + Alt + D** (*Describe*) explicitly captures the monitor containing the cursor and sends the screenshot, cursor/monitor context, foreground process/title, and temporary question to the currently selected provider. Gemini is selected for development.

When a provider locates the elements its answer refers to, CursorAI draws a highlight and label over each one on the monitor that was captured, and the buddy switches to its Guiding state. The overlay clears when the next turn begins and otherwise dismisses itself after twelve seconds. It is excluded from screen capture for the same reason the buddy is: highlights drawn for one question must not appear in the screenshot for the next.

Asking for coordinates roughly triples latency — measured 25.5s for a plain description against a real desktop versus 66s to 73s with element detection — which is why the request timeout is 120 seconds. It costs no extra requests: the answer and the coordinates arrive in the same call.

The temporary vision question is: “What am I looking at, and what is under my cursor?” The returned text is currently written to the terminal used to launch CursorAI with `dotnet run`, together with the model, latency, and token counts the provider reported; no chat or response UI exists yet.

While a request is in flight it owns the buddy's visual state: the idle/listening toggle is refused rather than cancelling the Thinking animation, and when the request ends the state that was current beforehand is restored.

If another application already owns one of these shortcuts, CursorAI starts anyway and lists the unavailable ones on stderr; only losing all three is fatal.

The defaults are three-key combinations rather than four-key chords, so each is one left-hand motion, and the letters are mnemonic: **A**sk, **S**creenshot, **D**escribe, **Q**uit.

Only one CursorAI process may run per signed-in user. A second launch exits immediately with an explanation rather than starting, because two instances compete for the same global hotkeys and the loser reports them as owned by “another application” — true, but confusing when that application is itself.

Shortcuts are configurable because `RegisterHotKey` is a system-wide, first-come claim: whichever process registers a combination first owns it until it exits, and Windows offers no way to ask who that is. Measured on the development machine, only `Ctrl + Alt + Space` and `Alt + F4` were already owned out of 51 combinations tried, while all 26 `Ctrl + Alt + <letter>` combinations were free. The earlier `Ctrl + Alt + Space` default was simply unlucky; input-method switchers and launcher tools commonly claim it.

```powershell
$env:CURSORAI_HOTKEY_ACTIVATE = "Ctrl + Alt + A"
$env:CURSORAI_HOTKEY_CAPTURE  = "Ctrl + Alt + S"
$env:CURSORAI_HOTKEY_VISION   = "Ctrl + Alt + D"
$env:CURSORAI_HOTKEY_QUIT     = "Ctrl + Alt + Q"
```

On keyboard layouts that use **AltGr** to type accented characters, AltGr is `Ctrl + Alt`, so these defaults can collide with ordinary typing. Override them if that applies to you.

Accepted forms are any combination of `Ctrl`, `Alt`, `Shift`, and `Win` plus one key: a letter, a digit, `F1`-`F24`, or a name such as `Space`, `Enter`, `Delete`, or `PageDown`. Malformed text stops startup and names the variable at fault; a combination with no modifier is rejected, because it would capture that key system-wide.

## Voice

Speech recognition and synthesis both run **entirely on this machine** through the Windows speech engines. Microphone audio never leaves the computer, so voice stays inside the local trust boundary and consumes none of the vision provider's request quota. Only the screenshot and the recognised question cross the boundary, exactly as the **V** shortcut already does.

A spoken turn waits up to eight seconds for speech to begin, then abandons the attempt rather than holding the microphone open. The spoken reply has markdown stripped first, since a synthesiser otherwise reads `**Export PDF**` as “star star Export PDF star star”, and is shortened to roughly 600 characters at a sentence boundary.

Voice depends on machine state CursorAI does not control: an installed speech recognition language, a microphone, an output device. Any of those missing is reported at startup and disables only voice; vision by hotkey keeps working.

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

The Gemini free tier allows **20 requests per day, per model**. Each **Ctrl + Alt + D** press, and each spoken turn, spends one. When the daily allowance is gone the API answers 429 with `Retry-After` set hours ahead; CursorAI reports that and stops rather than retrying, because retrying would spend further requests to receive the same refusal.

Because the quota is per model, `GEMINI_MODEL` is also the way to keep testing after exhausting one. Measured on 2026-10-08: a full-resolution 1920x1080 call to `gemini-3.5-flash` completed in 4.4 seconds; `gemini-3.8-flash` under heavy load exceeded 30 seconds, which is why the request timeout is 60 seconds.

`OPENAI_API_KEY` is needed only if application composition is changed to select `OpenAIVisionProvider`:

```powershell
$env:OPENAI_API_KEY = "<your-api-key>"
```

These assignments affect only the current PowerShell process/session and child processes started from it.

## Privacy boundary

The buddy window is excluded from screen capture via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`. Before this, the window—layered because of `AllowsTransparency`—was pulled into CursorAI's own screenshots by `CAPTUREBLT`, covering an 80x80 pixel region at cursor+(20,20) and occluding the very thing the cursor pointed at.

Screenshots and foreground window titles can contain sensitive or private information. No automatic capture, background monitoring, periodic screenshots, or screen recording occurs. The local **Ctrl + Alt + S** shortcut never uploads data. Only an explicit **Ctrl + Alt + D** press or a spoken turn started with **Ctrl + Alt + A** sends the current monitor screenshot and selected context to the active provider—currently Google’s Gemini Interactions API. A spoken turn additionally sends the recognised text of your question; the audio itself never leaves the machine, because recognition runs on-device. CursorAI explicitly sends `store=false`, so the request is stateless and is not retained as an Interaction object for later retrieval or continuation. This setting does not make broader claims about Google’s processing or data-use practices.

Developers using Gemini’s free tier should avoid testing with sensitive or private screens and review Google’s current Gemini API data-use terms before submitting real sensitive information.
