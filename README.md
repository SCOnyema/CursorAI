# CursorAI

CursorAI is a Windows-native AI desktop companion being built incrementally.

Current status: **V0.3.0 — Screen Capture Foundation**

Technology stack: C#, .NET 10, WPF

V0.1 Cursor Companion and V0.2 Controls & State are complete. Press **Ctrl + Alt + Space** to toggle between `Idle` and `Listening`.

For development verification, press **Ctrl + Alt + Shift + S** to capture the entire monitor containing the mouse cursor. The screenshot is held in memory and a PNG copy is saved locally under `artifacts/captures/`.

Screenshots may contain sensitive information visible on the selected display. Capture occurs only when the user explicitly presses the development shortcut. Captures remain local: they are not uploaded, sent to an AI service, or processed by a vision model.

AI, vision inference, continuous capture, voice, visual guidance overlays, and UI Automation are not implemented yet.
