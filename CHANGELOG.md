## [1.4.3] - 2026-11-23

### Fixed
- Keyword groups 1–8 honor Notepad++ rules: quoted phrases (e.g. `"TOOL CALL"`), Prefix mode, longest match, then group priority Keywords1 before Keywords8. Concatenated NC words like `G40G49` highlight correctly.

## [1.4.2] - 2026-11-23

### Fixed
- UDL comments parse the Notepad++ packed form (e.g. `00% 03( 04)`), so EIA/G-code `(...)` and `%` comments use the COMMENTS / LINE COMMENTS styles.

## [1.4.1] - 2026-11-23

### Fixed
- Language menu lists every user-defined language. Files with bad fontName characters from Notepad++ still load.

## [1.4.0] - 2026-11-23

### Added
- Language menu after Edit. It loads the Notepad++ user-defined languages from user_defined_language and colors text NC programs with the selected template.

## [1.3.9] - 2026-11-23

### Changed
- The toolbar machine buttons are labeled INTEGREX, Turning Center, Machining Center, and INTE-e Machine.

## [1.3.8] - 2026-11-23

### Fixed
- Opening an HTML file no longer shows the Internet Explorer security warning.

## [1.3.7] - 2026-11-23

### Changed
- The welcome page says Please Open NC Program.

## [1.3.6] - 2026-11-23

### Fixed
- After a drop, the last file is selected and shown. The welcome page no longer stays on screen.

## [1.3.5] - 2026-11-23

### Fixed
- Dropping several files on the preview opens every file in its own tab.

## [1.3.4] - 2026-11-23

### Fixed
- Dragging files onto the preview opens them again. Several files in one drop each get a tab.

## [1.3.3] - 2026-11-23

### Fixed
- Dropping several files on the window opens each one in a tab.

## [1.3.2] - 2026-11-23

### Changed
- Opening a Matrix program no longer asks which machine to use. It opens as Machining (VRX). The toolbar buttons still change the machine.

## [1.3.1] - 2026-11-23

### Fixed
- The window stays open after launch. The tab refresh no longer crashes the process.

## [1.3.0] - 2026-11-23

### Added
- Open and drop can take several files. Each file opens in its own tab, and Close File closes the selected tab.

## [1.2.20] - 2026-11-23

### Changed
- Help → About shows the MIT license for MazatrolViewerAero. The previous company credit has been removed.

## [1.2.19] - 2026-11-23

### Fixed
- File → Open shows an HTML file as the page. A dropped HTML file is the open file, so Ctrl+W and Close File clear it.

## [1.2.18] - 2026-11-23

### Changed
- File no longer has Save As. Export still writes the list as HTML or plain text.
- File → Close File, and Ctrl+W, clear the open program and return to the welcome page.

## [1.2.17] - 2026-11-23

### Changed
- Setup no longer has Browser Setup. The dialog that opened Windows Internet Options has been removed.

## [1.2.16] - 2026-11-23

### Changed
- Setup, Other Options no longer has an English or Japanese choice. Menus, dialogs, and the program list are English only.

## [1.2.15] - 2026-11-23

### Changed
- The window and the exe use the sharper red M icon in Assets\App.ico.

## [1.2.14] - 2026-11-23

### Changed
- The window and the exe use the red M on yellow supplied for this program. The previous icon file has been removed.

## [1.2.13] - 2026-11-23

### Added
- A README describes how to run the viewer, which program files it opens, and which structure table each extension uses.

## [1.2.12] - 2026-11-20

### Changed
- The program can be published as one exe that already contains .NET, so it runs from a copied folder with no install. Settings and the program page stay in a Data folder next to that exe.

## [1.2.11] - 2026-11-20

### Fixed
- A MAZ machining program shows as a bordered grid with its real units. 18203300 lists material, offset, pallet change, index, work coordinates, line, pocket, and manual program units with their tools and shapes, then sub program and end.

### Changed
- MAZ and PBM values keep four decimals, like the Matrix list. A tool approach now reads -102.7354 where it read -102.735.

## [1.2.10] - 2026-11-20

### Fixed
- A T6M turning program shows as a bordered grid with the same units as Mazatrol Viewer. 7005400 lists material, M codes, bar, edge, groove, copy, drill, manual program, and end units with their shape lines.

## [1.2.9] - 2026-11-20

### Fixed
- A PBM machining program shows as a bordered grid with its real units. HAKIMSQ lists material, offset, index, work coordinates, face mill, line left, tools, shapes, and end values.

## [1.2.8] - 2026-11-20

### Fixed
- A Matrix turning program shows the same units as Mazatrol Viewer. 18226200 lists head, manual program, bar, facing, groove, drill, transfer, and end values, including tool names and figure corners.

## [1.2.7] - 2026-11-20

### Fixed
- Matrix line-center units show their mode, tools, and shape. Unit 20 of 18226200 is LINE CTR in ZY, with the end-mill rows and the two LINE figures.

## [1.2.6] - 2026-11-20

### Fixed
- A Matrix pocket program shows the same columns as Mazatrol Viewer. 001-Pocket-Mill-Matrix lists material as OD-MAX and ID-MIN, pocket mode XC, rough and finish end mills, figure attribute CLOSED, and end return ZERO PT.

## [1.2.5] - 2026-11-20

### Changed
- The toolbar no longer shows Cut, Copy, Paste, or About.

## [1.2.4] - 2026-11-20

### Fixed
- An NC file dropped on the program page is accepted and opened. The page no longer refuses the drop or asks to save the file.

## [1.2.3] - 2026-11-20

### Fixed
- Dropping an NC file onto the window opens it. The page no longer asks to save the file.

## [1.2.2] - 2026-11-20

### Fixed
- Copy keeps only the highlighted lines. Ctrl+C no longer replaces a selection with the whole program.

## [1.2.1] - 2026-11-20

### Fixed
- A manual program row splits G1 and G2 into their own cells, and each axis value stays in its DATA column. The tool row uses the same number of cells as the lines under it.

## [1.2.0] - 2026-11-20

### Changed
- Program values are shown in a grid with a white border around every cell.
- Copy places a tab between cells, so the program pastes into a spreadsheet one value per column.

## [1.1.2] - 2026-11-20

### Fixed
- Shape lines for a pocket or face are read from the program records, so 19011400 lists every LINE, CW, CCW, and SQR instead of a short guessed table.
- Manual program, subprogram, and end units are included. 19011400 now shows MANU PRO, SUB PRO 19011401, and END, with the same unit numbers as the original viewer.
- Work offset addition and angle, and the pitch columns, show 0 or @ when the program leaves them unset.

## [1.1.1] - 2026-11-17

### Fixed
- Tool sequence rows now include both rough and finish tools, with the tool name, number, approach, type, and the rest of the SNo columns.
- A face-mill sequence such as 19011400 shows R 1 F-MILL and F 2 F-MILL, including XBI, 177, 2600, and 0.8.

## [1.1.0] - 2026-11-17

### Added
- Program list lines now include the operation header, such as UNo. UNIT DEPTH SRV-Z BTM WAL FIN-Z FIN-R INTER-R CHMF.
- Facing and pocket rows show depth, finish, tool diameter, approach, and feed values in those columns.

### Changed
- Measure columns share one width, so a coordinate stays under its header.

## [1.0.1] - 2026-11-17

### Changed
- Removed the startup password. The viewer opens directly to the main window.
- The window and executable use the icon from MazatrolViewer.exe.

### Fixed
- After the password dialog the process exited, so the main window never appeared.
- Saving settings failed when the window position was not a number.

## [1.0.0] - 2026-11-17

### Added
- MazatrolViewerAero, a .NET 10 Windows desktop viewer for Mazatrol programs.
- Open, drag-and-drop, and recent files for .m6m, .pba, .t6m, .t6i, .pbb, .pbd, .pbe, .pbf, .pbm, .pbg, .maz, .m6p, and .nc.
- File export, Save As, print, print preview, and page setup.
- Edit commands: cut, copy, paste, select all, and find.
- Machine setup for M640M, M640MPro, M640T, M640MT, M640MTPro, and MATRIX families, including detail bits.
- Toolbar machine modes INTE, TC, MC, and INTEe, plus HD1 and HD2.
- Language, display options, browser options, and an About dialog.
- Program page written to Data\Mazpro.html.

[1.2.5]: https://github.com/compare/v1.2.4...v1.2.5
[1.2.4]: https://github.com/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/compare/v1.1.2...v1.2.0
[1.1.2]: https://github.com/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/compare/v1.0.0
