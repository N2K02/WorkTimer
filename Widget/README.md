# WorkTimer taskbar widget

This companion uses [WidBar](https://apps.microsoft.com/detail/9PKLDNM83TP9) to show two lines directly in available Windows 11 taskbar space:

```text
07:42 - 16:42
6:43 left
```

Click the widget for the tracker's details and controls. The two main buttons allow 1-click clock-in and clock-out. **Edit clock-in** opens an inline time picker once clocked in; clock-out is disabled until then. Weekend rules are unchanged.

**Open history** opens the existing CSV in its associated application. **Start tracker with Windows** controls the tracker's existing startup registration. The options card includes **Restart tracker** and **Exit tracker**. When the tracker is unavailable, the primary button becomes **Start tracker**. It launches only the locally registered WorkTimer executable. The tracker registers its path when it starts, so launch it normally once if it has moved.

Warnings, including an Excel-locked history file, appear in the flyout and add a warning indicator to the taskbar preview. The widget also shows not-clocked-in (blinking red), clocked-in (green), overtime (orange), and clocked-out (neutral gray) states.

## Build and register

The widget targets Windows 11 x64 and .NET 10. Its separate project uses WidBar.SDK 2.3.0 and Windows App SDK 2.6.4. The normal WinForms application keeps its existing dependencies. Build tools are restored from NuGet; the application's icon supplies the package logos.

```powershell
powershell -NoProfile -File Widget\Build.ps1
powershell -NoProfile -File Widget\Install.ps1
```

Local registration requires Windows Developer Mode. The installer script checks it without changing Windows settings. The MSIX in `Widget/AppPackages/` is unsigned; use the script to register the built development layout. Keep the build directory in place while the widget is installed.

Install WidBar from the Microsoft Store. In WidBar's **Layout** page, refresh the catalog, find **WorkTimer**, and drag it into free taskbar space.

The widget is the taskbar display and control surface. The tracker runs in the background.

## Data and validation

The tracker atomically publishes `widget-status.json` in `%USERPROFILE%\AppData\Roaming\WorkTimer`. It publishes on display, action-availability, preference, or warning changes and at least every 30 seconds. The widget polls every second while visible. It does not write clock-in state or history directly. Actions use a local named pipe restricted to the same Windows user, elevation level, and sign-in session. The tracker validates a fixed set of commands and runs them on its UI thread. It makes no network requests.

After three minutes without a valid update, or if the file is missing, unreadable, or invalid, the widget shows **Tracker unavailable**, disables clock/history/settings controls, and offers **Start tracker**. If a workday was already started today, the widget automatically launches the tracker and synthesizes the live countdown.

Run the regression tests:

```powershell
dotnet run --project tests\WorkTimer.RegressionTests.csproj -c Release
```
