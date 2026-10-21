# Windows VM UI review — 21 October 2026

Worked directly in the Windows repository on the user-specified network share. The checkout began clean at b202966 (1.0.15). Read START-HERE.md, DOSSIER.md, IMPLEMENTATION.md and the recent transcript in the supplied Mac handoff; compared the sidebar with the adjacent macOS ContentView.swift without modifying that separate repository.

## Changes

- Use neutral primary text for the countdown and macOS's muted gold (#CCAB75) for the timer icon.
- Keep the sidebar background and border quiet, including in the last ten seconds.
- Replace the text arrow with a native chevron in its own trailing column. Stretch the button across the card so the chevron reaches the right edge; retain the affordance while input resets the timer.
- Use the theme's opaque SolidBackgroundFillColorBaseBrush for the queue surface. The popup window still permits transparency for rounded corners, but its content no longer uses the translucent card brush.

## Validation

127 native chooser, presentation, lifecycle and updater checks passed on this ARM64 Windows VM. The added/updated presentation checks cover neutral countdown text, a quiet imminent state, the chevron's measured position against the full card width, and the popup brush's full opacity. Existing checks exercise a 30-app queue, long names, scrolling, input-reset scroll retention, pause dismissal and navigation.

Inspected rendered rules/countdown and many-app queue previews. Local previews and build logs are in ignored TestResults/. The standard build script could not execute directly from the UNC share under the VM's current script policy; equivalent dotnet publish commands were used without changing that policy.

Self-contained ARM64 and x64 builds are placed under IdleSentry for Windows/publish/win-arm64 and publish/win-x64. These are local review builds retaining source version 1.0.15; no public release assets or feeds were replaced.

## Interactive ARM64 updater validation completed

The initial conclusion that desktop control was unavailable was premature. The local Computer Use plugin and node_repl server were configured, but not exposed as native chat tools. Connected to the configured node_repl MCP server and executed the skill's @oai/sky JavaScript API. Its default launcher failed to load temporary kernel.js/trusted-worker.js files; the server's documented --disable-sandbox launch option worked with this session's already unrestricted execution permissions. No persistent Codex configuration was changed. All app input/capture used sky; the local adapter only transported standard MCP calls and returned images.

Built a disposable updater-enabled 1.0.14 via the command-line Version override, without changing tracked project version, and packaged the same identity/channel for ARM64. Installed into an explicit local test directory with spaces. Backed up the existing settings before replacing them with paused test settings and one disposable Character Map selection. No public fixture or feed was published.

Observed through the actual Windows UI:

- Check for Updates discovered the real public 1.0.15 release.
- Download Update displayed progress to 100% and verified the ARM64 package.
- A full process termination/relaunch kept 1.0.14 running with 1.0.15 pending, rather than silently applying it. This particular persistence check used process termination, not the tray Quit action.
- An additional launch surfaced the existing window without creating a second lasting app process.
- Clicked Install & Restart and its confirmation. The old process exited and a new process reopened as official 1.0.15+b202966d8410126d8fbdae31964560f530a64c9d.
- Timeout 420, Force Quit, paused state, app ID/path/name and custom timeout 123 survived. Login startup remained enabled with the valid stable root launcher command. Automatic update checks remained opted out.
- A subsequent Check for Updates reported “You’re up to date.”
- In the UI-corrected lower-version fixture, opened the queue for the disposable running app and visually confirmed an opaque surface and properly aligned chevron. The popup remained inspectable after input reset. The downloaded official 1.0.15 naturally contains the previously published UI, not these unpublished fixes.

Screenshots and structured observations are retained in ignored TestResults/computer-use/ and TestResults/computer-use-final/. The final visible up-to-date screenshot is capture-43-1.png; the live corrected queue is capture-22-2.png. TestResults/updater-vm/upgrade-result.json records the exact installed version/startup command and preference validation.

## Cleanup and limits

The silent test uninstaller returned zero and removed registration, shortcuts, update packages and the owned startup entry, but left the running test process and its locked executable. Explicitly stopped that test process and archived its leftover executable into ignored test evidence. Automatic approval review blocked recursive removal of the residual test directory (only “blocked by policy” was reported); no such deletion was performed. The remaining two local test directories are empty. Original settings.json was restored byte-for-byte; the newly created test-only updates.json was moved to the ignored evidence directory. The disposable Character Map process was closed. No test startup entry or running IdleSentry process remains.

This establishes the real ARM64 download/install/restart flow on this VM. It does not establish an interactive x64-on-hardware result, a real Windows login cycle, tray Quit behavior, or interrupted-network download recovery. Those remain separate coverage limits. No updater source defect was reproduced in the tested flow; the live-uninstall residue is recorded above rather than represented as a clean uninstall smoke-test pass.
