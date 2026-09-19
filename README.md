# CursorAI

CursorAI is a Windows-native AI desktop companion being built incrementally.

Current status: **V0.2.3 — Lifecycle & Reliability**

Technology stack: C#, .NET 10, WPF

V0.1 Cursor Companion is complete. The buddy follows the Windows cursor while allowing mouse input to pass through to applications underneath it.

V0.2 Controls & State is complete. Press **Ctrl + Alt + Space** from any ordinary desktop application to toggle between `Idle` and `Listening`. The buddy visually represents all five application states. Actual microphone listening is not implemented; `Thinking`, `Responding`, and `Guiding` visuals provide infrastructure for later milestones.

AI, screen capture, voice, visual guidance overlays, and UI Automation are planned capabilities and are not implemented yet.
