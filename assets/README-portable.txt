AMD Chipset Updater (portable)
==============================

Run AMDChipsetUpdater.exe. No installation needed.

- Detects your AMD platform (socket and chipset) and finds the matching
  chipset driver page on amd.com.
- Compares the installed AMD Chipset Software version with the latest one
  and shows "Updated" or "Update available".
- "Install" / "Update" downloads the official installer from amd.com,
  checks that it is digitally signed by AMD, and runs it
  (Windows asks for administrator permission).

If the socket or chipset is not detected (or is wrong), pick it from the
"Platform" drop-downs; "Reset to auto" goes back to automatic detection.

When a new version of this app is published on GitHub, a popup offers to
update it in place when you open it.

"Check for updates when Windows starts" adds a per-user startup entry that
runs a quick silent check, shows a notification if an update is available,
and then exits. If you move this folder, open the app once and the startup
entry is updated. Untick the option before deleting the app.

Requirements: Windows 10/11 with .NET Framework 4.8 (included in Windows).

Unofficial tool, not affiliated with AMD.
