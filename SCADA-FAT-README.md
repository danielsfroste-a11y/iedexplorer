# IEC 61850 SCADA / FAT monitor (initial revision)

This branch adds a dockable, intentionally minimal FAT overview to the existing IEDExplorer WinForms application. It **does not** introduce a second MMS connection or alter the MMS protocol engine.

## Quick workflow
1. Connect to a real IED using the existing IP/port controls and wait until the MMS data model appears.
2. Open **SCADA / FAT** from the main toolbar.
3. In **IED View (MMS)** or **IEC View (61850)**, drag individual leaf attributes onto the dashboard, or right-click an attribute and select **Add to SCADA / monitor**.
4. For controls, use a leaf `Oper.ctlVal`, `SBO.ctlVal`, or `SBOw.ctlVal` (including `ctlVal.i`/`ctlVal.f`). Right-click **Add to SCADA / command**, or drag it and explicitly select command.
5. Rename or remove a tile with its context menu; save/open `*.scada.xml` profiles.
6. Select **Auto read** for periodic MMS reads. Values can also update through the shared model from existing MMS Reports.

## Safety and limitations
- **Command execution is disabled by default.** Enabling it requires an explicit confirmation; each command requires a second confirmation plus the original CommandDialog.
- The original IEDExplorer command flow handles normal, SBO and enhanced-security variants; the SCADA view does **not** implement independent control writes.
- Profiles are bound to the hostname/IP used when the first signal is added. A profile loaded on another IED shows an **IED mismatch** and does not issue reads or commands. To test another device, use **New layout**, or make a profile for the new device.
- The dashboard is a **FAT test monitor**, not an operational SCADA system. It does not implement command interlocks, authorization roles, audit database, dual controls, alarm shelving, redundancy or fail-safe control logic.
- A successful MMS connection is not proof of GOOSE subscription/communication. GOOSE must be tested independently.
- The cards show **raw IEC 61850 values**; no semantic assumption is made that Boolean true means a closed breaker, trip or healthy status.
- Timestamp shown is when the UI **observed a changed value**, not the IED source timestamp. Do not use it as an event timestamp.
- The current revision is one active MMS session/IED at a time. Maximum 200 cards, reads are throttled to six per 1.5 seconds and paused if the MMS send queue is busy.
- This branch retargets the legacy application to **.NET Framework 4.8** and retains its existing third-party dependencies. A compatible .NET Framework runtime is required on the target Windows computer.

## Architecture
- `Views/ScadaView.cs`: independent WinForms `DockContent`, layout persistence, low-volume polling, and safe command affordance.
- `Views/WindowManager.cs`: owns one persistent dashboard and routes selections/commands.
- `Views/IedTreeView.cs`: exposes context actions and routes controls through its existing `DoSendCommandClick`, including SBO dialogs.
- `Views/MainWindow.cs`: toolbar shortcut.
- `IEDExplorer.csproj`: registers new source.

The remote model is bound via `CommAddress` values, not UI tree-node IDs; this permits rebinding on reconnect. The existing `Iec61850State`, `NodeData`, `Scsm_MMS_Worker` and `Iec61850Controller` remain the only data/command path.

## Manual FAT smoke test (requires real IED or simulator)
1. Launch: confirm the SCADA tab opens without needing an IED.
2. Connect to REX615; ensure tree and MMS read work.
3. Add an `MX` analog and `ST` status leaf by drag/drop and context menu.
4. Enable Auto read; change an actual IED value, verify the card refreshes. Disable Auto read and verify a configured Report can still update the model.
5. Save and reload a profile; ensure captions and order remain.
6. Disconnect: the panel must go offline and commands must become disabled.
7. Reconnect same IED: values must rebind to the new data model.
8. Open saved profile while connected to a different host: no reads or controls should be sent.
9. Add a legitimate `ctlVal` to a command tile. With controls disabled the button must be inactive; enabling asks for consent. Press the button: verify selection of correct IED and existing command dialog. Do **not** issue a physical operation outside an approved FAT setup.
10. Confirm existing MMS tree, Reports, SCL and GOOSE windows still function.

## Future increments
The current result intentionally uses compact tiles for quick FAT work. A next phase can add a true synoptic single-line canvas (busbars, disconnectors, breakers, alarm lamps, arrows, typed analog readouts), with configurable status mappings, engineering units, quality/time indicators and multiple IED connections. These must be bound to actual datatypes and control models rather than guessed from path names.

## Repository review notes
The repository inventory contains 1,025 tree entries (files and folders, including generated ASN.1 model, DockPanel library, TreeViewAdv, embedded DLLs and resources). The control/UI/MMS data flows have been traced through `Program`, `Views/MainWindow`, `WindowManager`, `IedTreeView`, `IedDataView`, `PollView`, `Node*`, `Iec61850State`, `Iec61850Controller` and `Scsm_MMS_Worker`. This is an architectural review, **not** a claim of line-by-line auditing all third-party/generated code.

Legacy risks to address separately:
- Fixed in this branch: `NodeData.FC` previously walked parents without advancing to the next parent, risking an infinite loop.
- Fixed in this branch: `WindowManager.UnBindFromCapture` previously attempted to unsubscribe newly created anonymous lambdas, leaving old handlers attached.
- `NodeData.ValueTag` is shared mutable UI state; multiple subscribers can interfere.
- No existing automated build workflows or tests were present on the default branch when reviewed.

GPL-3.0-or-later notice: this addition follows the upstream project's license; preserve copyright and licensing notices in redistributed builds.

## Automated local EXE delivery

The GitHub Actions workflow builds on `windows-latest` using Visual Studio/MSBuild. It then transfers the compiled output as a **one-day temporary Actions artifact** to the user's connected self-hosted Windows runner. The local runner does **not** need Visual Studio/MSBuild or an altered PowerShell execution policy. It only runs Windows CMD.

After a fully successful run, the permanent local launcher is:

```text
C:\actions-runner\IEDExplorer-FAT\Start-IEDExplorer-FAT.cmd
```

The matching executable stays in `C:\actions-runner\IEDExplorer-FAT\builds\<commit-id>\IEDExplorer.exe` and the current installed commit is recorded in `BUILD-INFO.txt`. A failed compile or transfer does not update the launcher. To avoid exhausting Actions storage, the temporary handoff artifact is deleted after successful local installation. Previous local builds remain accessible and can be removed manually if no longer needed. The workflow **does not auto-launch a Windows GUI** inside the unattended runner session.
