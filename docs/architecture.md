# Architecture Roadmap

This roadmap distinguishes implemented milestones from planned functionality.

- V0.1 — Cursor companion: complete
- V0.2 — Global controls and application state: complete
- V0.3 — Screen capture and vision: in progress; local capture context and the provider-neutral vision boundary are implemented
- V0.4 — Voice input/output: planned
- V0.5 — Visual guidance overlays: planned
- V0.6 — Windows UI Automation grounding: planned
- V1.0 — First complete working version: planned

## Vision Pipeline

```text
ScreenCaptureService
        ↓
CapturedFrame
        ↓
VisionService
        ↓
VisionRequest
        ↓
IVisionProvider
        ↓
Future multimodal provider (not implemented)
```

Everything through `VisionRequest` is currently local. The external provider boundary is future work and is not operational. A future implementation must make screenshot transmission explicit and must obtain secrets from local configuration, environment variables, or a user-secret mechanism—never hard-coded or committed credentials.
