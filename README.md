# CursorAI

CursorAI is a Windows-native AI desktop companion being built incrementally.

Current status: **V0.3.2 — Vision Provider Architecture**

Technology stack: C#, .NET 10, WPF

V0.1 Cursor Companion and V0.2 Controls & State are complete. Local, explicit monitor capture with cursor and foreground-window context is available through **Ctrl + Alt + Shift + S**.

`VisionRequest` is the provider-neutral input boundary for a future multimodal provider. `VisionService` encodes an existing captured frame to PNG bytes entirely in memory, composes its capture context, and delegates through `IVisionProvider`. `VisionResult` currently contains only provider-neutral response text.

No AI provider or network integration exists in V0.3.2, and no screenshot, prompt, window title, process name, or other metadata leaves the machine. The existing development capture continues to save only local PNG and JSON files beneath `artifacts/captures/`.

Future provider integration must be explicit because screenshots and window metadata can contain highly sensitive information. API keys must never be hard-coded or committed to Git; future secrets must come from an appropriate local configuration, environment-variable, or user-secret mechanism.
