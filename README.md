# MazatrolViewerAero

**Current version:** 1.4.3

MazatrolViewerAero is a Windows desktop program that opens a Mazatrol file and lists it the way Mazatrol Viewer does. The list is a bordered grid: unit name, tool rows, and figure rows, with the values that were entered on the control.

The program only reads the file you open. It does not connect to a machine, it does not need a password, and it does not send the program, the path, or the settings anywhere.

## What the window does

Open one or more programs with **File → Open**, with a file named on the command line, or by dragging files onto the window. Each file gets a tab. **File → Close File** (Ctrl+W), or the × on a tab, closes that tab. The welcome page returns when the last tab closes. The last eight files stay under **File → Recent files**.

**Language** lists the Notepad++ user-defined languages from the `user_defined_language` folder next to the exe (G-code, Siemens, Heidenhain, DMIS, and the others you supplied). Choosing a language colors a text NC program with that template. Opening a file can pick the language from its extension.

The preview is a black page in a fixed-width font. Unit names are green, shape codes such as LINE, CW, and CCW are yellow, and the numbers are white. A blank row separates a unit from the next unit when the column headings change.

**Edit → Copy** (Ctrl+C) copies the cells you highlighted. Cells on one row are separated by tabs, so the selection pastes into a spreadsheet as columns. If nothing is highlighted, the copy is the whole program. **Edit → Select All** (Ctrl+A) and **Edit → Find** (Ctrl+F) work on the page. **File → Export** writes the list as HTML or as plain text. **File → Print** prints the page.

The toolbar has Open, Print, and four machine buttons (INTEGREX, Turning Center, Machining Center, INTE-e Machine) plus HD1 and HD2. Those buttons redraw the open program as that Matrix family or head. **View** shows or hides the toolbar and the status bar.

**Setup → Machine Setup** stores the machine name, inch display, and the related switches for each family. **Setup → Other Options** sets the program-information line, the binary line, and the data folder. A Matrix program opens as Machining (VRX). The toolbar buttons INTEGREX, Turning Center, Machining Center, and INTE-e Machine change that machine. **Help → About** shows the MIT license.

## Requirements

| Use | What you need |
|---|---|
| Build and run from source | Windows, and the .NET 10 SDK |
| Run the portable exe | 64-bit Windows. No .NET install |

The portable exe is built for 64-bit Windows only.

## Run from source

```bat
cd C:\Users\Public\Documents\MazatrolViewerAero\MazatrolViewerAero
dotnet build -c Release
```

The program is:

`bin\Release\net10.0-windows\MazatrolViewerAero.exe`

From the project folder, `dotnet run -c Release` builds and starts it. There is no password.

## Portable copy

One exe, with the .NET runtime inside it:

```bat
cd C:\Users\Public\Documents\MazatrolViewerAero\MazatrolViewerAero
dotnet publish -c Release -p:PublishProfile=win-x64-singlefile
```

That writes:

`publish\MazatrolViewerAero-win-x64\MazatrolViewerAero.exe`

Copy that exe to the folder you want and run it. The first start unpacks the native display libraries into the Windows temp folder. That unpack is the runtime starting, not an install. The program still does not phone home.

Settings and the generated page are stored beside the exe, under `Data`. See [Files written on this PC](#files-written-on-this-pc).

## Program files it opens

**File → Open** lists these extensions:

`.m6m` `.pba` `.t6m` `.t6i` `.pbb` `.pbd` `.pbe` `.pbf` `.pbm` `.pbg` `.maz` `.m6p` `.nc` `.html` `.htm`

A binary program is checked against the type stored at byte `0x20`, except a `.maz` file, which is a headerless record list and is recognized by its first eight bytes. A text file (`.m6p`, `.nc`, or any file whose start is plain text) is shown as numbered ISO lines. An `.html` or `.htm` file is shown as the page itself.

| Extension | Kind on the title line | Header type |
|---|---|---|
| `.m6m` | M640M | 35 |
| `.pba` | M640MPro | 36 |
| `.t6m`, `.t6i` | M640T/MT | 23 |
| `.pbb` | M640MTPro | 24 |
| `.pbd`, `.pbe`, `.pbf` | MATRIX | 37 |
| `.pbm` | PBM | 38 |
| `.pbg` | PBG | 64 |
| `.maz` | MAZ | none (records start at byte 0) |
| `.m6p`, `.nc` | ISO | text |
| `.html`, `.htm` | HTML | the page itself |

## Extension, record layout, and structure file

The files in `Decoding\Structures` are one table per record layout, not one table per extension. Several extensions share a layout, so they share one XML file. The table is embedded in the exe at build time.

| Extension | Record layout | Structure file |
|---|---|---|
| `.m6m` | M6M machining | `m6m_structure.xml` |
| `.pbf`, `.pbe` | same Matrix turning records | `pbf_structure.xml` |
| `.pbd`, `.pbm`, `.maz` | same Matrix machining records | `pbd_structure.xml` |
| `.pbg` | its own layout | `pbg_structure.xml` |
| `.t6m`, `.t6i` | 50-byte turning records | none |

How each row is used:

- **`.m6m`** is drawn from `m6m_structure.xml` by the M6M reader.
- **`.pbf` and `.pbe`** are the same Matrix turning records. Both read `pbf_structure.xml`. There is no separate PBE table.
- **`.pbd`, `.pbm`, and `.maz`** are the same 100-byte Matrix machining records (type byte, unit number, a mask of entered fields, measures as int32 / 10000). A `.maz` file is that list with no file header, so the first record is at byte 0. A `.pbd` file is drawn from `pbd_structure.xml`. A `.pbm` or `.maz` file is drawn by the machining reader in `Decoding\LayoutProgram.Machining.cs`. That reader still loads `pbd_structure.xml`, and uses it only for a record type the reader does not already know.
- **`.pbg`** has its own table, `pbg_structure.xml`.
- **`.t6m` and `.t6i`** do not use these XML files. The turning reader in `Decoding\LayoutProgram.Turning.cs` reads the 50-byte records directly. A T6M file has two 250-byte headers, then the records from byte 500. Measures are int32 / 1000.

These extensions open, and they do not have a structure file:

| Extension | Why there is no structure file |
|---|---|
| `.pba`, `.pbb` | Accepted by the open dialog and checked by header type. They are not keys in the structure catalog, so they have no XML table. |
| `.m6p`, `.nc` | ISO text. The page is the source lines with line numbers, not a unit grid. |

## Files written on this PC

Nothing is written to the registry, and nothing is sent on the network.

| File | Purpose |
|---|---|
| `Data\settings.json` | Window size, recent files, and machine setups. The folder is next to `MazatrolViewerAero.exe`. |
| `Data\Mazpro.html` | The page last shown. |
| `Data\Chk` | A small check file in the data folder. |

On the first run of a new copy, an older settings file under `%LocalAppData%\MazatrolViewerAero\settings.json` is read once. After that, the program saves beside the exe and does not write the profile copy again.

`settings.json` stores the data folder as a full path. A copied exe that brings an old `Data\settings.json` with it keeps writing `Mazpro.html` to that old path. Delete that settings file, or set the data folder in **Setup → Other Options** to the `Data` folder next to the new exe.

## Project layout

| Path | Role |
|---|---|
| `MazatrolViewerAero.csproj` | .NET 10 WPF project. The version is the `<Version>` value. |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | Window, menus, open, export, copy, drag and drop. |
| `App.xaml.cs` | Startup. `--render input output.html` writes one preview and exits. `--render-dir inputDir outputDir` does the same for a folder. |
| `Decoding\MazDocument.cs` | Identifies the file and builds the page. |
| `Decoding\LayoutProgram.cs` | Grid drawing, and the M6M, PBD, PBE, PBF, and PBG readers. |
| `Decoding\LayoutProgram.Machining.cs` | PBM and MAZ reader. |
| `Decoding\LayoutProgram.Turning.cs` | T6M and T6I reader. |
| `Decoding\Structures\` | The four XML layout tables. |
| `Settings\ViewerSettings.cs` | Settings load and save. |
| `Ui\` | Dialogs and menu captions. |
| `Properties\PublishProfiles\win-x64-singlefile.pubxml` | The portable publish. |
| `CHANGELOG.md` | What changed in each version. |
| `how to run.txt` | Short build and run notes. |

## Version history

See [CHANGELOG.md](CHANGELOG.md).
