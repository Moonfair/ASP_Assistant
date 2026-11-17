# macOS Desktop Port Implementation Plan

**Goal:** Deliver an Apple Silicon macOS desktop build with the Windows application's catalog, tracking, lineup, recognition, overlay, account and settings features.

**Architecture:** Keep the existing WPF application as the Windows entry point. Share its data, view models, recognition logic and persistence through `ASPAssistant.Core`; add an Avalonia entry point for macOS. Replace Windows capture and window APIs at the platform boundary, and replace GDI image operations with SkiaSharp on macOS. Use the same JSON catalogues and MaaFramework OCR resources.

**Tech stack:** .NET 9, Avalonia, MaaFramework 5.8.1, SkiaSharp, macOS `screencapture`, Swift CoreGraphics and WebKit helpers, an Objective-C Cocoa window bridge, xUnit.

## Work order

1. Record and verify the repository baseline. Complete the exact revision download and verify every Git blob hash.
2. Add a `net9.0` core target with the macOS Maa runtime. Build it and identify unsupported APIs.
3. Add tested macOS ADB discovery and screenshot handling. Provide explicit game source selection.
4. Add macOS image operations and window capture so existing preparation, HUD and shop recognition can run.
5. Build the Avalonia side panel: operator and equipment filters, details, tracking, lineup management and editing.
6. Wire live recognition and overlay marker placement to the selected game window or ADB viewport.
7. Add website account, sharing, history, settings, updates and feedback flows.
8. Package a `.app`, document permissions and dependencies, run unit tests and exercise each feature on macOS.

The Windows WPF project remains separately buildable. Each stage should be verified before the next stage begins.
