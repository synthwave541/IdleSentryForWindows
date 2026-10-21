# Windows UI review — 21 October 2026

Reviewed the actual ARM64 app with Computer Use: Dashboard, Monitored Apps, app chooser, Inactivity Rules, System & Startup and About. Checked the normal 960 × 680 and minimum 840 × 580 layouts. This is a quick design review, not a full accessibility certification.

## Fixed: inset hover surface in the sidebar

The padded outer card contained a separately themed Button, producing a smaller second rectangle during hover (and disabled control styling). The Button now owns the entire rounded surface and padding, with full-card hover/pressed states and a visible focus indicator. The read-only state retains the same single surface. The earlier neutral countdown, trailing chevron and opaque queue remain intact.

128 native checks passed, including measured equality of the full card and button surface. Computer Use also clicked the outer padding in an isolated snapshot of the real MainWindow: it opened the queue and highlighted the whole card. That snapshot never started monitoring or loaded user preferences. Evidence: ignored TestResults/preview-run/capture-69-1.png. The actual app was rebuilt and reopened for preview.

## Findings for follow-up

1. **Accessibility: missing action names.** In the live Monitored Apps UI Automation tree, the Add Application action is just “button”; its visible text is a separate child. The remove action likewise has no Name, only the generic help text “Remove from protection.” Set explicit names; include the affected app in the removal name, such as “Remove Antigravity from monitored apps.” Evidence: result-55.json / result-58.json. Microsoft requires a meaningful accessible name for interactive elements: https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-checklist
2. **Readability: very small secondary text.** Sidebar labels use 10–10.5 DIP and several descriptions use 11–11.5 DIP. At the smaller window size, the explanatory copy is noticeably dense. Increase important descriptions/status text and check text scaling; no universal minimum font size is claimed here. Microsoft guidance: https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements
3. **Feedback clarity when updates are unavailable.** In this unpackaged preview, About correctly explains that Setup is needed and disables the update actions, but still presents an enabled, checked “Check for updates automatically” control. Disable or hide that preference while updates are unavailable, so the UI does not imply automatic checking is active. This concerns the unavailable state, not the successful installed updater tested earlier. Evidence: capture-65-1.png / result-65.json.
4. **Visual semantics: red running-app count.** A healthy running-app count uses an error-red badge. A neutral/accent badge would avoid suggesting an alert. This is a design recommendation, not a formal accessibility violation. Source: RunningAppsBadge; observed in the isolated preview and the earlier live monitored-app queue.

## What held up

Navigation selected the correct page; the main controls remained usable at the minimum window size; About provided scrolling; the chooser had clear loading/results, selected-app exclusion, search and Cancel affordances; the settings copy explained unsaved-change risk. No monitored-app choices, timer values or startup preferences were changed during this review.

High contrast, Windows text-size scaling, Narrator speech and a complete keyboard-only workflow were not fully tested. No unmeasured contrast failure or keyboard failure is claimed. The remaining findings above have been reported, not implemented as an unrequested redesign. Public release assets remain unchanged pending the user's green light.
