# CursorAI

CursorAI is a Windows-native AI desktop companion being built incrementally.

Current status: **V0.2.0 — Application State Model**

Technology stack: C#, .NET 10, WPF

V0.1 Cursor Companion is complete. The buddy follows the Windows cursor while allowing mouse input to pass through to applications underneath it.

V0.2.0 introduces state infrastructure only:

- `Idle` — available and not actively processing a request.
- `Listening` — receiving user input; microphone functionality is not implemented.
- `Thinking` — processing a request; AI functionality is not implemented.
- `Responding` — presenting a response; text-to-speech is not implemented.
- `Guiding` — visually guiding the user; guidance overlays are not implemented.

Global controls and later AI, voice, overlay, and UI Automation capabilities are not implemented yet.
