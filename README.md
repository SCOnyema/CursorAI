# CursorAI

CursorAI is a Windows-native AI desktop companion being built incrementally.

Current status: **V1.0 — Working Without a Terminal**

Technology stack: C#, .NET 10, WPF

V0.1 Cursor Companion and V0.2 Controls & State are complete. V0.3.3 now includes two implementations of the provider-neutral vision boundary:

- `GeminiVisionProvider`, selected by default, using `gemini-3.8-flash` through the Gemini Interactions API
- `OpenAIVisionProvider`, using `gpt-5.4-mini` through the Responses API
- `OpenAICompatibleVisionProvider`, reaching any endpoint that speaks the OpenAI chat-completions API

There is no automatic fallback. The active provider is chosen at startup from `CURSORAI_VISION_PROVIDER`, or in the settings window (**Ctrl + Alt + K**), which offers `gemini` and `openai` along with their keys and models. A provider change applies the next time CursorAI starts; a changed key or model applies to the next request. `compatible` is configured through environment variables only:

| Value | Reaches | Credential |
|---|---|---|
| `gemini` (default) | Google Gemini Interactions API | `GEMINI_API_KEY` |
| `openai` | OpenAI Responses API | `OPENAI_API_KEY` |
| `compatible` | anything speaking the OpenAI chat-completions API | `CURSORAI_COMPATIBLE_API_KEY` (optional) |

```powershell
$env:CURSORAI_VISION_PROVIDER = "openai"
$env:OPENAI_API_KEY           = "<key>"
$env:OPENAI_MODEL             = "gpt-5.4-mini"   # optional; this is the default
```

Like Gemini, OpenAI requests are sent with storage turned off (`store=false`), so the response is not kept for later retrieval. The request timeout is 120 seconds on every provider.

### Other providers through the compatible endpoint

`compatible` exists so that one provider reaches many vendors: OpenRouter, Groq, Together, Azure OpenAI, and local model servers such as Ollama or LM Studio all expose `/chat/completions` with image content parts. Only the base URL and model change.

```powershell
$env:CURSORAI_VISION_PROVIDER     = "compatible"
$env:CURSORAI_COMPATIBLE_BASE_URL = "https://openrouter.ai/api/v1"
$env:CURSORAI_COMPATIBLE_MODEL    = "<a vision-capable model at that endpoint>"
$env:CURSORAI_COMPATIBLE_API_KEY  = "<key>"
```

Neither value has a default, because which models exist depends entirely on which endpoint is configured. The API key is optional, since local servers usually need none.

Chat-completions is used rather than OpenAI's newer Responses API precisely because third-party endpoints implement the former and almost none implement the latter. That is what makes this provider portable, and it is why `openai` remains a separate provider.

**Pointed at a local model server this sends nothing off the machine at all**, which removes both the request quota and the external trust boundary. Speech is already fully local, so that would make a build where nothing leaves the computer. The trade is real: local vision models read fine screen text less reliably, and guidance coordinates depend on exactly that.

Two cautions. Model identifiers and pricing move — this project has already had a default model head for retirement, and many older OpenAI identifiers stop working on 2026-10-23, so verify before committing to one. And nothing in the `openai` or `compatible` paths has been exercised against a live endpoint: no key and no local server were available when they were written, so treat the first real call as the test.

## Installing

CursorAI publishes as a single self-contained executable, so no .NET runtime install is needed:

```powershell
dotnet publish .\src\CursorAI.App\CursorAI.App.csproj -c Release -p:PublishProfileActive=true -o .\dist
```

That produces one file of roughly 78 MB at `.\dist\CursorAI.App.exe`. Copy it anywhere and run it. The build is unsigned, so Windows SmartScreen will warn on first launch.

On first run CursorAI shows what leaves your machine and asks you to agree before anything is sent. Declining is supported: local-only screenshots keep working and nothing is transmitted. It then opens settings if no key is configured, and shows a card listing every shortcut.

A **notification-area icon** is CursorAI's persistent surface: right-click it for every action with its shortcut beside it, or double-click to type a question. Without it an installed build would present a cursor-following eye and nothing else, with no way to learn that any shortcut exists.

Diagnostics are written to `%LOCALAPPDATA%\CursorAI\logs\cursorai-YYYYMMDD.log`, kept for seven days. Everything the console shows goes there too, so a problem is reportable even when CursorAI was never launched from a terminal. Logs can include window titles, which is why they are pruned rather than kept indefinitely.

On first run, with no key configured, CursorAI opens its settings window and says so in the response panel. Paste a Gemini API key there; a free one can be created at [aistudio.google.com/apikey](https://aistudio.google.com/apikey). Press **Ctrl + Alt + K** to reopen settings at any time.

Keys are encrypted with DPAPI for your Windows account and stored in `%LOCALAPPDATA%\CursorAI\settings.json`. The plaintext key never touches disk. That is better than a plain configuration file and is not a secret vault: anything running as your Windows account can decrypt it, which is unavoidable for a desktop application that has to use the key itself.

Environment variables still take precedence over anything saved in settings, so an existing development workflow is unaffected. The settings window names any field the environment is currently controlling rather than appearing to accept a value that will not take effect.

## Development shortcuts

- **Ctrl + Alt + A** (*Ask*) starts a spoken turn: CursorAI listens, captures the monitor containing the cursor, asks the active vision provider your spoken question, then reads the answer aloud. Where speech recognition is unavailable it falls back to toggling the Idle and Listening states.
- **Ctrl + Alt + S** (*Screenshot*) performs a local-only capture of the monitor containing the cursor and writes a PNG plus JSON context beneath `artifacts/captures/`. It sends nothing externally.
- **Ctrl + Alt + T** (*Type*) opens the response panel focused on its input box, so a question can be typed instead of spoken.
- **Ctrl + Alt + K** (*Keys*) opens settings, where API keys, the provider, and the model can be set without a terminal.

Every action is also on the notification-area icon's menu, with its shortcut shown beside it.

A request in flight can be abandoned: press **Esc** in the response panel, click **Cancel** there, or use *Cancel the current request* on the tray menu. While a request is running Esc cancels it; otherwise Esc dismisses the panel. Cancelling stops CursorAI waiting but cannot un-send a request that has already left, so the provider's daily allowance may still have been spent — the panel says so rather than implying a refund.
- **Ctrl + Alt + Q** (*Quit*) shuts CursorAI down. The buddy is click-through with no taskbar entry, so this is the only way to stop it from the keyboard.
- **Ctrl + Alt + D** (*Explain*) captures the monitor containing the cursor and asks the active provider to explain what you are looking at and what you can do there, starting with the part you are pointing at.

When a provider locates the elements its answer refers to, CursorAI draws a highlight and label over each one on the monitor that was captured, and the buddy switches to its Guiding state. The overlay clears when the next turn begins and otherwise dismisses itself after twelve seconds. It is excluded from screen capture for the same reason the buddy is: highlights drawn for one question must not appear in the screenshot for the next.

Where the focused application exposes its contents through Windows UI Automation, each highlight is snapped from the model's estimate onto the element's exact bounds. Coverage varies enormously by application: measured 2026-10-12, Notepad exposed 42 usable elements in about 200 ms, while an Electron window exposed only its three window-chrome buttons behind nameless panes spanning the whole window. Applications that expose nothing usable keep the model's own coordinates, which are typically within a couple of pixels, so guidance never regresses because of this step.

Asking for coordinates roughly triples latency — measured 25.5s for a plain description against a real desktop versus 66s to 73s with element detection — which is why the request timeout is 120 seconds. It costs no extra requests: the answer and the coordinates arrive in the same call.

## Response panel

Answers, provider metadata, and failures appear in a panel beside the cursor, and the panel shows the current activity so a twenty-second request does not look like a hang. Press **Esc** to dismiss it.

It also takes typed input. That exists because dictation measurably mishears: open recognition returned “what about Michael and so” for a question about Kotter’s change model. Typing avoids that without giving up on-device speech, which costs nothing and sends no audio anywhere. A typed question runs through exactly the same pipeline as a spoken one.

Before this panel existed, everything went to the console, which only exists when CursorAI is launched from a terminal — so running the executable directly produced a cursor-following eye that reported nothing at all, including its own errors.

## Conversations and follow-ups

Every question, whether spoken, typed or from **Ctrl + Alt + D**, is answered with the last few questions and answers of the conversation as context, so “and then what?” or “where is that?” makes sense. The current screenshot is always attached and wins over anything said earlier; earlier screenshots are never kept or re-sent.

- Up to six earlier exchanges are kept, as text, **in memory only**. Nothing is written to disk and quitting forgets it.
- A conversation ends after ten minutes without a question, or immediately with *New conversation* on the tray menu.
- The response panel shows `follow-up to N earlier` whenever history was sent, so it is never sent unnoticed.
- Only answered questions are remembered; a failed or cancelled one is not.

The model is told what CursorAI is and how to answer: answer the question asked rather than narrating the screen, mention the cursor only when it matters, and give numbered steps for “how do I”. Before this the prompt was screen geometry plus the question, and Ctrl + Alt + D asked a fixed development question, *“What am I looking at, and what is under my cursor?”*, which is why answers kept opening with exactly that.

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
$env:CURSORAI_HOTKEY_TYPE     = "Ctrl + Alt + T"
$env:CURSORAI_HOTKEY_SETTINGS = "Ctrl + Alt + K"
```

On keyboard layouts that use **AltGr** to type accented characters, AltGr is `Ctrl + Alt`, so these defaults can collide with ordinary typing. Override them if that applies to you.

Accepted forms are any combination of `Ctrl`, `Alt`, `Shift`, and `Win` plus one key: a letter, a digit, `F1`-`F24`, or a name such as `Space`, `Enter`, `Delete`, or `PageDown`. Malformed text stops startup and names the variable at fault; a combination with no modifier is rejected, because it would capture that key system-wide.

## Voice

Speech recognition uses **Whisper** and synthesis uses **Piper**, both running locally. Neither sends anything anywhere. Microphone audio never leaves the computer, so voice stays inside the local trust boundary and consumes none of the vision provider's request quota. Only the screenshot and the recognised question cross the boundary, exactly as the **V** shortcut already does.

Voice input is **push-to-talk**: hold **Ctrl + Alt + A** while speaking and release to transcribe. Whisper cannot stream, so the utterance needs an explicit end; this also removes the old engine's voice activity detection, which truncated sentences mid-way. A single utterance is capped at twenty seconds.

### A better voice

The built-in Windows voice sounds robotic. Piper is a local neural synthesiser that sounds markedly better, and like the Whisper model it is supplied rather than bundled — **nothing is downloaded automatically**. With Piper absent, CursorAI uses the Windows voice and prints what to install.

```powershell
# extract piper_windows_amd64.zip from github.com/rhasspy/piper/releases (about 21 MB) into:
#   %LOCALAPPDATA%\CursorAI\tts\piper
# then add a voice (about 60 MB) from huggingface.co/rhasspy/piper-voices:
#   en/en_US/lessac/medium/en_US-lessac-medium.onnx  and  .onnx.json
$env:CURSORAI_PIPER_VOICE = "en_US-amy-medium"   # optional; default is en_US-lessac-medium
```

The engine binary is the MIT-licensed archived release, deliberately: the maintained repository is GPL-3.0 and ships only a Python wheel for Windows. CursorAI runs Piper as a separate process and distributes no part of it, so it contains no GPL code either way. Voices from `rhasspy/piper-voices` are MIT.

Piper is kept running rather than started per answer. Measured on the development machine: started per answer it took 6.74s for 9.8s of speech, of which only 1.55s was inference; held open, audio begins after about 0.65s on the first answer and 0.45s thereafter. The Windows voice begins effectively instantly, so the half second buys the better voice.

Markdown is stripped before playback either way, since a synthesiser reads `**Export PDF**` aloud as “star star Export PDF star star”.

### Whisper models

Models are read from `%LOCALAPPDATA%\CursorAI\models` and **nothing is ever downloaded automatically** — these files are hundreds of megabytes, which is not a decision software should make behind a hotkey press. With no model present CursorAI falls back to the legacy Windows recognizer and prints exactly which file to fetch and where to put it.

```powershell
mkdir $env:LOCALAPPDATA\CursorAI\models
curl -L -o $env:LOCALAPPDATA\CursorAI\models\ggml-small.en.bin `
  https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin
$env:CURSORAI_WHISPER_MODEL = "base.en"   # optional; default is small.en
```

Sizes verified by HTTP HEAD on 2026-10-14: `tiny.en` 74 MB, `base.en` 141 MB, `small.en` 465 MB, `medium.en` 1463 MB.

Transcription of a 5.7-second utterance, measured on the development machine: `base.en` 2.9s warm, `small.en` 8.4s warm. Both transcribed the synthesized test phrase perfectly, so that measurement says nothing about their relative accuracy on real speech with background noise and an accent — which is where the larger model is expected to earn its size. `small.en` is the default for that reason; set `CURSORAI_WHISPER_MODEL=base.en` to trade accuracy for roughly three times the speed. The spoken reply has markdown stripped first, since a synthesiser otherwise reads `**Export PDF**` as “star star Export PDF star star”, and is shortened to roughly 600 characters at a sentence boundary.

The Whisper engine's own native DLLs (about 1.9 MB, MIT) are embedded in the published executable and written to `%LOCALAPPDATA%\CursorAI\runtimes` on first start. Whisper.net only looks for them beside the application, which a single-file build cannot satisfy, so before this every published voice turn recorded the question and then failed with `FileNotFoundException`. They are checked at startup, so if they still cannot load, CursorAI says so and falls back to the Windows recognizer instead of failing mid-question.

Voice depends on machine state CursorAI does not control: an installed speech recognition language, a microphone, an output device. Any of those missing is reported at startup and disables only voice; vision by hotkey keeps working.

## API keys

Keys are normally set in the settings window described above. For development they can also come from the environment, which takes precedence:

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

These assignments affect only the current PowerShell process/session and child processes started from it.

## Privacy boundary

The buddy window is excluded from screen capture via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`. Before this, the window—layered because of `AllowsTransparency`—was pulled into CursorAI's own screenshots by `CAPTUREBLT`, covering an 80x80 pixel region at cursor+(20,20) and occluding the very thing the cursor pointed at.

Agreement is recorded per disclosure version, so a future change to what is sent asks again rather than inheriting the old answer. The response panel names the provider while a request is in flight, so the moment your screen leaves the machine is visible rather than implied.

Screenshots and foreground window titles can contain sensitive or private information. No automatic capture, background monitoring, periodic screenshots, or screen recording occurs. The local **Ctrl + Alt + S** shortcut never uploads data. Only an explicit **Ctrl + Alt + D** press, a typed question, or a spoken turn started with **Ctrl + Alt + A** sends the current monitor screenshot and selected context to the active provider: Google’s Gemini Interactions API by default, or OpenAI’s Responses API or a configured compatible endpoint when one of those is selected. A spoken turn additionally sends the recognised *text* of your question. Each request also carries the text of up to six earlier questions and answers from the current conversation, held in memory only and dropped after ten minutes idle; agreeing to the disclosure before this was added does not cover it, so it is asked again. **No audio ever leaves the machine, in either direction.** Recordings are held in memory, transcribed locally by Whisper, and discarded; they are never written to disk. Spoken replies are synthesised locally by Piper, or by the Windows voice, from text that is already on this machine. No cloud speech service is used for recognition or for synthesis. CursorAI explicitly sends `store=false` to Gemini and to OpenAI, so the request is not retained for later retrieval or continuation. That is what CursorAI asks for, not a claim about either provider’s broader processing or data-use practices. Compatible endpoints vary in what they accept, so nothing is promised there.

Developers using Gemini’s free tier should avoid testing with sensitive or private screens and review Google’s current Gemini API data-use terms before submitting real sensitive information.
