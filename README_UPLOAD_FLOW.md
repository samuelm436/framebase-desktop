Upload Flow (Desktop)

- PresentMonRecorder captures frametimes efficiently when the game is detected.
- Live Monitor shows a rolling average of the last 2 seconds (FPS + 1% low derived from that window).
- Every 1.5 seconds, InputActivityMonitor checks for input; the UI shows "Aktiv" vs "Inaktiv" accordingly.
- If 10 consecutive checks (15 seconds) detect no activity, the recording pauses and the session is uploaded.
- A session counter runs from the beginning and resets after an upload.
- On upload failure, a detailed error message is displayed.
- Unrelated parts of the old upload process are removed or bypassed; only RAM buffers are used, no CSV files.
