using System.IO;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using Microsoft.Win32;
using MazatrolViewerAero.Decoding;
using MazatrolViewerAero.Language;
using MazatrolViewerAero.Settings;
using MazatrolViewerAero.Ui;

namespace MazatrolViewerAero;

public partial class MainWindow : Window
{
    private readonly ViewerSettings _settings;
    private readonly HashSet<string> _dropIgnore = new(StringComparer.OrdinalIgnoreCase);
    private readonly BrowserFileDrop _fileDrop;
    private MenuItem _recent = null!;
    private MazDocument? _document => SelectedPage?.Document;
    private OpenPage? SelectedPage => (DocumentTabs.SelectedItem as TabItem)?.Tag as OpenPage;
    private WebBrowser ActiveViewer => Viewer;

    public MainWindow(ViewerSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        if (!double.IsNaN(settings.Left) && !double.IsNaN(settings.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.Left;
            Top = settings.Top;
            Width = settings.Width;
            Height = settings.Height;
            if (settings.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        _fileDrop = new BrowserFileDrop(OpenDroppedFiles);
        DocumentTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ShowSelected);
        BuildChrome();
        ApplyChromeVisibility();
        Viewer.Navigating += OnBrowserNavigating;
        Viewer.LoadCompleted += (_, _) => EnableFileDrop();
        Drop += OnDropFiles;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
        Closing += (_, _) =>
        {
            ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
            SavePlacement();
        };
        SourceInitialized += (_, _) => EnableFileDrop();
        Loaded += (_, _) =>
        {
            SilenceBrowser();
            EnableFileDrop();
        };
        ShowWelcome();
    }

    public void OpenPath(string path) => OpenPaths([path]);

    private void OpenDroppedFiles(IReadOnlyList<string> paths)
    {
        _dropIgnore.Clear();
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _dropIgnore.Add(path);
            }
        }

        OpenPaths(paths);
        Dispatcher.BeginInvoke(() => _dropIgnore.Clear(), DispatcherPriority.ApplicationIdle);
    }

    public void OpenPaths(IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            return;
        }

        var documents = new List<MazDocument>();
        var errors = new List<string>();
        foreach (var path in files)
        {
            var document = MazDocument.TryOpen(path, out var failure);
            if (document == null)
            {
                errors.Add(Path.GetFileName(path) + ": " + FailureText(failure));
                continue;
            }

            documents.Add(document);
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "File Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (documents.Count == 0)
        {
            return;
        }

        TabItem? select = null;
        foreach (var document in documents)
        {
            var tab = FindTab(document.Path) ?? CreateTab(document);
            select = tab;
            _settings.Remember(document.Path);
        }

        ShowTabs();
        if (select != null)
        {
            DocumentTabs.SelectedItem = select;
            if (!ReferenceEquals(DocumentTabs.SelectedItem, select))
            {
                DocumentTabs.SelectedIndex = DocumentTabs.Items.IndexOf(select);
            }
        }

        _settings.Save();
        BuildChrome();
        var page = select?.Tag as OpenPage;
        if (page != null)
        {
            ShowDocument(page: page);
        }
    }

    private void BuildChrome()
    {
        MainMenu.Items.Clear();
        MainToolBar.Items.Clear();

        var file = Menu("File");
        file.Items.Add(Item("Open...\tCtrl+O", (_, _) => Open(), "Ctrl+O"));
        file.Items.Add(Item("Close File\tCtrl+W", (_, _) => CloseFile(), "Ctrl+W"));
        file.Items.Add(Item("Export", (_, _) => Export()));
        file.Items.Add(new Separator());
        file.Items.Add(Item("Print...\tCtrl+P", (_, _) => BrowserCommand(6), "Ctrl+P"));
        file.Items.Add(Item("Print Preview", (_, _) => BrowserCommand(7)));
        file.Items.Add(Item("Print Setup...", (_, _) => BrowserCommand(8)));
        file.Items.Add(new Separator());
        _recent = new MenuItem { Header = "Recent files" };
        FillRecent();
        file.Items.Add(_recent);
        file.Items.Add(new Separator());
        file.Items.Add(Item("Exit", (_, _) => Close()));

        var edit = Menu("Edit");
        edit.Items.Add(Item("Cut\tCtrl+X", (_, _) => BrowserCommand(11), "Ctrl+X"));
        edit.Items.Add(Item("Copy\tCtrl+C", (_, _) => CopySelection(), "Ctrl+C"));
        edit.Items.Add(Item("Paste\tCtrl+V", (_, _) => BrowserCommand(13), "Ctrl+V"));
        edit.Items.Add(Item("Select All\tCtrl+A", (_, _) => BrowserCommand(17), "Ctrl+A"));
        edit.Items.Add(new Separator());
        edit.Items.Add(Item("Find\tCtrl+F", (_, _) => BrowserCommand(32), "Ctrl+F"));

        var language = Menu("Language");
        var currentLanguage = SelectedPage?.SyntaxLanguage ?? _settings.SyntaxLanguage;
        language.Items.Add(LanguageItem("(None)", "", currentLanguage));
        foreach (var udl in UdlCatalog.Shared.All)
        {
            language.Items.Add(LanguageItem(udl.Name, udl.Name, currentLanguage));
        }

        if (UdlCatalog.Shared.All.Count == 0)
        {
            language.Items.Add(new MenuItem { Header = "(no user_defined_language folder)", IsEnabled = false });
        }

        var view = Menu("View");
        view.Items.Add(CheckItem("Toolbar", _settings.ShowToolbar, value =>
        {
            _settings.ShowToolbar = value;
            ApplyChromeVisibility();
            _settings.Save();
        }));
        view.Items.Add(CheckItem("Status Bar", _settings.ShowStatusBar, value =>
        {
            _settings.ShowStatusBar = value;
            ApplyChromeVisibility();
            _settings.Save();
        }));

        var setup = Menu("Setup");
        var machines = new MenuItem { Header = "Machine Setup.." };
        foreach (var family in FamilyInfo.All)
        {
            var captured = family;
            machines.Items.Add(Item(family.English, (_, _) => EditFamily(captured)));
        }

        setup.Items.Add(machines);
        setup.Items.Add(Item("Other Options", (_, _) => EditOptions()));

        var help = Menu("Help");
        help.Items.Add(Item("About MazatrolViewerAero", (_, _) => AeroDialogs.About(this)));

        MainMenu.Items.Add(file);
        MainMenu.Items.Add(edit);
        MainMenu.Items.Add(language);
        MainMenu.Items.Add(view);
        MainMenu.Items.Add(setup);
        MainMenu.Items.Add(help);

        Tool("Open", Open);
        Tool("Print", () => BrowserCommand(6));
        MainToolBar.Items.Add(new Separator());
        Tool("INTEGREX", () => ReloadAs(1, "MatrixIntegrex"));
        Tool("Turning Center", () => ReloadAs(2, "MatrixTurning"));
        Tool("Machining Center", () => ReloadAs(3, "MatrixMachining"));
        Tool("INTE-e Machine", () => ReloadAs(4, "MatrixIntegrexE"));
        MainToolBar.Items.Add(new Separator());
        Tool("HD1", () => SetHead(1));
        Tool("HD2", () => SetHead(2));
        StatusText.Text = "Ready";
    }

    private void FillRecent()
    {
        _recent.Items.Clear();
        if (_settings.Recent.Count == 0)
        {
            _recent.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }

        foreach (var path in _settings.Recent)
        {
            var captured = path;
            _recent.Items.Add(Item(path, (_, _) =>
            {
                if (File.Exists(captured))
                {
                    OpenPath(captured);
                }
            }));
        }
    }

    private void Open()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All Mazatrol|*.m6m;*.pba;*.t6m;*.t6i;*.pbb;*.pbd;*.pbe;*.pbf;*.pbm;*.pbg;*.maz;*.m6p;*.nc;*.html;*.htm|"
                + "M640M MazPro (*.m6m)|*.m6m|M640MPro MazPro (*.pba)|*.pba|M640T MazPro (*.t6m)|*.t6m|"
                + "M640MT MazPro (*.t6i)|*.t6i|M640MTPro MazPro (*.pbb)|*.pbb|"
                + "MATRIX (*.pbd;*.pbe;*.pbf)|*.pbd;*.pbe;*.pbf|PBM (*.pbm)|*.pbm|PBG (*.pbg)|*.pbg|"
                + "MAZ (*.maz)|*.maz|ISO (*.m6p;*.nc)|*.m6p;*.nc|HTML (*.html;*.htm)|*.html;*.htm|All Files (*.*)|*.*",
            Title = "Open",
            Multiselect = true
        };
        if (!string.IsNullOrWhiteSpace(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
        {
            dialog.InitialDirectory = _settings.LastFolder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            OpenPaths(dialog.FileNames);
        }
    }

    private void CloseFile()
    {
        if (DocumentTabs.SelectedItem is TabItem tab)
        {
            CloseTab(tab);
        }
    }

    private void Export()
    {
        if (_document == null)
        {
            NeedProgram();
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Mazatrol Program",
            Filter = "Hyper Text File (*.html)|*.html|Text File (*.txt)|*.txt",
            FileName = Path.GetFileNameWithoutExtension(_document.Path)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var options = CurrentOptions();
        var body = dialog.FilterIndex == 2 ? _document.ToPlainText(options) : _document.ToHtml(options);
        File.WriteAllText(dialog.FileName, body);
    }

    private void EditFamily(FamilyInfo family)
    {
        var setup = _settings.Family(family.Key);
        if (AeroDialogs.EditFamily(this, family, setup))
        {
            _settings.Save();
            if (_document != null)
            {
                ShowDocument();
            }
        }
    }

    private void EditOptions()
    {
        if (!AeroDialogs.EditOptions(this, _settings))
        {
            return;
        }

        Directory.CreateDirectory(_settings.DataPath);
        _settings.Save();
        BuildChrome();
        ApplyChromeVisibility();
        if (_document != null)
        {
            ShowDocument();
        }
    }

    private void ReloadAs(int machineId, string familyKey)
    {
        _settings.MatrixMachineId = machineId;
        _settings.Save();
        if (_document == null)
        {
            NeedProgram();
            return;
        }

        ShowDocument(familyKey);
    }

    private void SetHead(int head)
    {
        _settings.Head = head;
        _settings.Save();
        if (_document == null)
        {
            NeedProgram();
            return;
        }

        ShowDocument();
    }

    private void ShowSelected()
    {
        if (SelectedPage != null)
        {
            ShowDocument(page: SelectedPage);
        }
    }

    private void ShowDocument(string? familyOverride = null, OpenPage? page = null)
    {
        page ??= SelectedPage;
        if (page == null)
        {
            return;
        }

        if (familyOverride != null)
        {
            page.FamilyOverride = familyOverride;
        }

        var document = page.Document;
        var browser = ActiveViewer;
        var options = CurrentOptions(familyOverride, page);
        var html = document.ToHtml(options);
        browser.NavigateToString(html);
        try
        {
            Directory.CreateDirectory(_settings.DataPath);
            File.WriteAllText(_settings.HtmlFile, html);
        }
        catch (Exception)
        {
            // The page is already shown when the data folder cannot be written.
        }

        UpdateStatus(page);
    }

    private void UpdateStatus(OpenPage? page = null)
    {
        page ??= SelectedPage;
        if (page == null)
        {
            Title = "MazatrolViewerAero";
            StatusText.Text = "Ready";
            StatusFile.Text = "";
            StatusMachine.Text = "";
            return;
        }

        var document = page.Document;
        Title = "MazatrolViewerAero - " + Path.GetFileName(document.Path);
        StatusText.Text = "Ready";
        StatusFile.Text = document.Path;
        if (document.HtmlPage)
        {
            StatusMachine.Text = "HTML";
            return;
        }

        var options = CurrentOptions(page: page);
        StatusMachine.Text = options.MachineName + (options.Inch ? "  INCH" : "  MM");
    }

    private void ShowWelcome()
    {
        DocumentTabs.Visibility = Visibility.Collapsed;
        var message = "Please Open NC Program";
        Viewer.NavigateToString("<html><body style=\"background:black;color:white;font-family:Segoe UI;padding:24px\"><h2>MazatrolViewerAero</h2><p>"
            + System.Net.WebUtility.HtmlEncode(message) + "</p></body></html>");
        Title = "MazatrolViewerAero";
        StatusText.Text = "Ready";
        StatusFile.Text = "";
        StatusMachine.Text = "";
    }

    private void ShowTabs()
    {
        if (DocumentTabs.Items.Count == 0)
        {
            ShowWelcome();
            return;
        }

        DocumentTabs.Visibility = Visibility.Visible;
    }

    private TabItem? FindTab(string path)
    {
        foreach (TabItem tab in DocumentTabs.Items)
        {
            if (tab.Tag is OpenPage page && string.Equals(page.Document.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    private TabItem CreateTab(MazDocument document)
    {
        var page = new OpenPage(document)
        {
            SyntaxLanguage = UdlCatalog.Shared.FindByExtension(document.Path)?.Name
                ?? (string.IsNullOrWhiteSpace(_settings.SyntaxLanguage) ? null : _settings.SyntaxLanguage)
        };
        var tab = new TabItem { Tag = page };
        tab.Header = TabHeader(Path.GetFileName(document.Path), document.Path, () => CloseTab(tab));
        DocumentTabs.Items.Add(tab);
        return tab;
    }

    private MenuItem LanguageItem(string header, string languageName, string? current)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = string.Equals(current ?? "", languageName, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += (_, _) => SetSyntaxLanguage(languageName);
        return item;
    }

    private void SetSyntaxLanguage(string languageName)
    {
        _settings.SyntaxLanguage = languageName;
        if (SelectedPage != null)
        {
            SelectedPage.SyntaxLanguage = string.IsNullOrWhiteSpace(languageName) ? null : languageName;
            ShowDocument(page: SelectedPage);
        }

        _settings.Save();
        BuildChrome();
    }

    private static object TabHeader(string name, string path, Action close)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = name,
            ToolTip = path,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 220,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var button = new Button
        {
            Content = "×",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Focusable = false,
            ToolTip = "Close",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, e) =>
        {
            e.Handled = true;
            close();
        };
        panel.Children.Add(button);
        return panel;
    }

    private void CloseTab(TabItem tab)
    {
        DocumentTabs.Items.Remove(tab);
        if (DocumentTabs.Items.Count == 0)
        {
            ShowWelcome();
        }
    }

    private ViewOptions CurrentOptions(string? familyOverride = null, OpenPage? page = null)
    {
        page ??= SelectedPage;
        var document = page?.Document;
        var overrideKey = familyOverride ?? page?.FamilyOverride;
        var key = overrideKey ?? FamilyKeyFor(document);
        var setup = _settings.Family(key);
        var machine = string.IsNullOrWhiteSpace(setup.Current) ? key : setup.Current;
        if (document?.TypeCode == 37 || overrideKey != null)
        {
            machine = _settings.MatrixMachineId switch
            {
                1 => _settings.Family("MatrixIntegrex").Current,
                2 => _settings.Family("MatrixTurning").Current,
                4 => _settings.Family("MatrixIntegrexE").Current,
                _ => _settings.Family("MatrixMachining").Current
            };
            if (string.IsNullOrWhiteSpace(machine))
            {
                machine = "MATRIX";
            }
        }

        return new ViewOptions(
            setup.Inch,
            _settings.InsertProgramInfo,
            _settings.AddBinaryLine,
            setup.ValidDetail,
            setup.R392,
            setup.R393,
            setup.R394,
            machine,
            _settings.Head,
            page?.SyntaxLanguage ?? _settings.SyntaxLanguage);
    }

    private static string FamilyKeyFor(MazDocument? document)
    {
        if (document != null && Path.GetExtension(document.Path).Equals(".t6i", StringComparison.OrdinalIgnoreCase))
        {
            return "M640MT";
        }

        return document?.TypeCode switch
        {
            23 => "M640T",
            24 => "M640MTPro",
            35 or 38 or 64 or 0 => "M640M",
            36 => "M640MPro",
            _ => "MatrixMachining"
        };
    }

    private void CopySelection()
    {
        if (_document == null)
        {
            NeedProgram();
            return;
        }

        var text = "";
        try
        {
            text = ActiveViewer.InvokeScript("aeroGridText") as string ?? "";
        }
        catch (Exception)
        {
            text = "";
        }

        if (text.Length == 0)
        {
            text = _document.ToPlainText(CurrentOptions());
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            BrowserCommand(12);
        }
    }

    private void BrowserCommand(int commandId)
    {
        if (_document == null && commandId is not 32)
        {
            if (commandId is 6 or 7 or 8 or 4)
            {
                NeedProgram();
                return;
            }
        }

        try
        {
            dynamic? browser = ActiveBrowser();
            if (browser == null)
            {
                throw new InvalidOperationException();
            }

            browser.ExecWB(commandId, 1, Type.Missing, Type.Missing);
        }
        catch (Exception)
        {
            if (ScriptFallback(commandId))
            {
                return;
            }

            if (commandId is 6 or 7 or 8)
            {
                MessageBox.Show(this, "The print job could not be started.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private bool ScriptFallback(int commandId)
    {
        try
        {
            switch (commandId)
            {
                case 6:
                case 7:
                    ActiveViewer.InvokeScript("aeroPrint");
                    return true;
                case 11:
                    ActiveViewer.InvokeScript("aeroCut");
                    return true;
                case 12:
                    ActiveViewer.InvokeScript("aeroCopy");
                    return true;
                case 13:
                    ActiveViewer.InvokeScript("aeroPaste");
                    return true;
                case 17:
                    ActiveViewer.InvokeScript("aeroSelect");
                    return true;
                case 32:
                    var query = AeroDialogs.AskFind(this);
                    if (query != null)
                    {
                        ActiveViewer.InvokeScript("aeroFind", query);
                    }

                    return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private object? ActiveBrowser()
    {
        var viewer = ActiveViewer;
        var property = viewer.GetType().GetProperty("ActiveXInstance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(viewer);
    }

    private void NeedProgram()
    {
        MessageBox.Show(this, "Please Open NC Program", Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string FailureText(OpenFailure failure) => failure switch
    {
        OpenFailure.TypeIncorrect => "Data Type Incorrect",
        OpenFailure.SizeIncorrect => "Data Size Incorrect",
        OpenFailure.OpenError => "Open File Error",
        _ => "This file isn't Maztrol Program!"
    };

    private void ApplyChromeVisibility()
    {
        MainToolBar.Visibility = _settings.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        MainStatus.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SilenceBrowser()
    {
        try
        {
            dynamic? browser = ActiveBrowser();
            if (browser != null)
            {
                browser.Silent = true;
                browser.RegisterAsDropTarget = false;
            }
        }
        catch (Exception)
        {
            // The host is still usable when script-error suppression is unavailable.
        }
    }

    private void EnableFileDrop()
    {
        SilenceBrowser();
        try
        {
            var window = new WindowInteropHelper(this).Handle;
            if (window != IntPtr.Zero)
            {
                _fileDrop.Attach(window);
            }
        }
        catch (Exception)
        {
            // The preview host still receives drops when the window handle is unavailable.
        }

        AttachFileDrop();
        Dispatcher.BeginInvoke(() =>
        {
            SilenceBrowser();
            AttachFileDrop();
        }, DispatcherPriority.Loaded);
    }

    private void SavePlacement()
    {
        _settings.Maximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.Save();
    }

    private void OnThreadFilterMessage(ref MSG message, ref bool handled)
    {
        const int keyDown = 0x0100;
        const int vkControl = 0x11;
        if (handled || !IsActive || message.message != keyDown)
        {
            return;
        }

        var control = (GetKeyState(vkControl) & 0x8000) != 0;
        if (control && message.wParam == (IntPtr)KeyInterop.VirtualKeyFromKey(Key.W))
        {
            handled = true;
            Dispatcher.BeginInvoke(CloseFile);
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O:
                    Open();
                    e.Handled = true;
                    break;
                case Key.W:
                    CloseFile();
                    e.Handled = true;
                    break;
                case Key.P:
                    BrowserCommand(6);
                    e.Handled = true;
                    break;
                case Key.X:
                    BrowserCommand(11);
                    e.Handled = true;
                    break;
                case Key.C:
                    CopySelection();
                    e.Handled = true;
                    break;
                case Key.V:
                    BrowserCommand(13);
                    e.Handled = true;
                    break;
                case Key.A:
                    BrowserCommand(17);
                    e.Handled = true;
                    break;
                case Key.F:
                    BrowserCommand(32);
                    e.Handled = true;
                    break;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnBrowserNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (e.Uri is not { IsFile: true } uri)
        {
            return;
        }

        var path = uri.LocalPath;
        if (_dropIgnore.Count > 0)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        if (File.Exists(path))
        {
            Dispatcher.BeginInvoke(() => OpenPath(path));
        }
    }

    private void AttachFileDrop(WebBrowser? browser = null)
    {
        try
        {
            browser ??= ActiveViewer;
            var property = typeof(HwndHost).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
            if (property?.GetValue(browser) is IntPtr hwnd && hwnd != IntPtr.Zero)
            {
                _fileDrop.Attach(hwnd);
            }
        }
        catch (Exception)
        {
            // A dropped file still opens when the page starts navigating to it.
        }
    }

    private void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            OpenDroppedFiles(files);
            e.Handled = true;
        }
    }

    private MenuItem Menu(string header) => new() { Header = header };

    private static MenuItem Item(string header, RoutedEventHandler click, string? gesture = null)
    {
        var item = new MenuItem { Header = header.Split('\t')[0] };
        if (gesture != null)
        {
            item.InputGestureText = gesture;
        }

        item.Click += click;
        return item;
    }

    private static MenuItem CheckItem(string header, bool value, Action<bool> changed)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = value };
        item.Click += (_, _) => changed(item.IsChecked);
        return item;
    }

    private void Tool(string caption, Action action)
    {
        var button = new Button { Content = caption, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(8, 3, 8, 3) };
        button.Click += (_, _) => action();
        MainToolBar.Items.Add(button);
    }
}

sealed class OpenPage(MazDocument document)
{
    public MazDocument Document { get; } = document;
    public string? FamilyOverride { get; set; }
    public string? SyntaxLanguage { get; set; }
}

sealed class BrowserFileDrop : IOleDropTarget
{
    private readonly Action<IReadOnlyList<string>> _open;

    public BrowserFileDrop(Action<IReadOnlyList<string>> open) => _open = open;

    public void Attach(IntPtr host)
    {
        if (host == IntPtr.Zero)
        {
            return;
        }

        AttachOwned(host);
        foreach (var child in ChildWindows(host))
        {
            AttachOwned(child);
        }
    }

    public int DragEnter(ComDataObject data, int keyState, long point, ref int effect)
    {
        effect = HasFiles(data) ? 1 : 0;
        return 0;
    }

    public int DragOver(int keyState, long point, ref int effect)
    {
        effect = 1;
        return 0;
    }

    public int DragLeave() => 0;

    public int Drop(ComDataObject data, int keyState, long point, ref int effect)
    {
        effect = 1;
        var files = FileNames(data);
        if (files.Count > 0)
        {
            var open = _open;
            var list = files.ToArray();
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => open(list));
        }

        return 0;
    }

    private void AttachOwned(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        RevokeDragDrop(hwnd);
        RegisterDragDrop(hwnd, this);
    }

    private static List<IntPtr> ChildWindows(IntPtr parent)
    {
        var children = new List<IntPtr>();
        EnumChildWindows(parent, (hwnd, _) =>
        {
            children.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return children;
    }

    private static bool HasFiles(ComDataObject data) => FileNames(data).Count > 0;

    private static List<string> FileNames(ComDataObject data)
    {
        try
        {
            if (new DataObject(data).GetData(DataFormats.FileDrop) is string[] names && names.Length > 0)
            {
                return names.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            }
        }
        catch (Exception)
        {
            // Fall through to the HDROP reader.
        }

        var files = new List<string>();
        var format = new FORMATETC
        {
            cfFormat = 15,
            ptd = IntPtr.Zero,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL
        };

        try
        {
            data.GetData(ref format, out var medium);
            try
            {
                AddDropNames(files, medium.unionmember);
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
        catch (Exception)
        {
            return files;
        }

        return files;
    }

    private static void AddDropNames(List<string> files, IntPtr drop)
    {
        if (drop == IntPtr.Zero)
        {
            return;
        }

        int count = DragQueryFile(drop, unchecked((uint)-1), null, 0);
        for (uint index = 0; index < count; index++)
        {
            int size = DragQueryFile(drop, index, null, 0);
            if (size <= 0)
            {
                continue;
            }

            var name = new StringBuilder(size + 1);
            if (DragQueryFile(drop, index, name, name.Capacity) > 0 && name.Length > 0)
            {
                files.Add(name.ToString());
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int DragQueryFile(IntPtr drop, uint index, StringBuilder? name, int capacity);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, IOleDropTarget target);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
}

[ComImport]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IOleDropTarget
{
    [PreserveSig]
    int DragEnter([MarshalAs(UnmanagedType.Interface)] ComDataObject data, int keyState, long point, ref int effect);

    [PreserveSig]
    int DragOver(int keyState, long point, ref int effect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int Drop([MarshalAs(UnmanagedType.Interface)] ComDataObject data, int keyState, long point, ref int effect);
}
