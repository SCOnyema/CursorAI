# CursorAI

CursorAI is a Windows-native AI desktop companion being built incrementally.

Current status: **V0.3.1 — Capture Context & Monitor Awareness**

Technology stack: C#, .NET 10, WPF

V0.1 Cursor Companion and V0.2 Controls & State are complete. Press **Ctrl + Alt + Space** to toggle between `Idle` and `Listening`.

For development verification, press **Ctrl + Alt + Shift + S** to capture the entire monitor containing the mouse cursor. Each explicit capture records physical monitor bounds and work area, primary-monitor status, physical and image-relative cursor coordinates, and best-effort foreground-window title and process name.

A paired PNG and JSON sidecar are saved locally under `artifacts/captures/`. Screenshots and foreground-window titles may contain sensitive information such as document names, page titles, and application context. Nothing is uploaded, sent to an AI service, or processed by a vision model.

AI, vision inference, continuous capture, voice, visual guidance overlays, and UI Automation are not implemented yet.
