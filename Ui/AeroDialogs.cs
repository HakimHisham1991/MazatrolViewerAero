using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MazatrolViewerAero.Settings;

namespace MazatrolViewerAero.Ui;

public static class AeroDialogs
{
    public static bool EditFamily(Window owner, FamilyInfo info, FamilySetup setup)
    {
        var window = new Window
        {
            Title = info.English,
            Width = 460,
            Height = info.HasDetail ? 280 : 240,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var machines = new ComboBox { Margin = new Thickness(12, 4, 12, 8), IsEditable = false };
        foreach (var name in setup.Machines)
        {
            machines.Items.Add(name);
        }

        machines.SelectedItem = setup.Current;
        var inch = new CheckBox { Content = "Inch Spec.", Margin = new Thickness(12, 4, 12, 4), IsChecked = setup.Inch };
        var submicron = new CheckBox { Content = "Rotation Axis Submicron Spec.", Margin = new Thickness(12, 4, 12, 4), IsChecked = setup.RotarySubmicron, Visibility = info.HasSubmicron ? Visibility.Visible : Visibility.Collapsed };
        var valid = new CheckBox { Content = "Valid Detail Setting", Margin = new Thickness(12, 4, 12, 4), IsChecked = setup.ValidDetail, Visibility = info.HasDetail ? Visibility.Visible : Visibility.Collapsed };
        var detail = new Button { Content = "Detail", Width = 120, Margin = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Visibility = info.HasDetail ? Visibility.Visible : Visibility.Collapsed };
        detail.IsEnabled = setup.ValidDetail;
        valid.Checked += (_, _) => detail.IsEnabled = true;
        valid.Unchecked += (_, _) => detail.IsEnabled = false;
        detail.Click += (_, _) => EditDetail(window, setup);

        var add = new Button { Content = "Add Machine (Read Parameter)", Margin = new Thickness(12, 8, 12, 4), Visibility = info.CanAdd ? Visibility.Visible : Visibility.Collapsed };
        var delete = new Button { Content = "Delete Machine", Margin = new Thickness(12, 4, 12, 4), Visibility = info.CanAdd ? Visibility.Visible : Visibility.Collapsed };
        add.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = info.ParameterFilter, Title = info.English };
            if (dialog.ShowDialog(window) != true)
            {
                return;
            }

            if (new FileInfo(dialog.FileName).Length == 0)
            {
                MessageBox.Show(window, "Open File Error", "File Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var suggested = Path.GetFileNameWithoutExtension(dialog.FileName);
            var name = AskName(window, suggested);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (!setup.Machines.Contains(name))
            {
                setup.Machines.Add(name);
                machines.Items.Add(name);
            }

            machines.SelectedItem = name;
        };
        delete.Click += (_, _) =>
        {
            if (machines.SelectedIndex <= 0 || machines.SelectedItem is not string name)
            {
                MessageBox.Show(window, "Can't Delete Default Machine", window.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(window, "Are you sure you want to delete this machine ?", "Delete Machine", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            setup.Machines.Remove(name);
            machines.Items.Remove(name);
            machines.SelectedIndex = 0;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        var ok = new Button { Content = "OK", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 88, IsCancel = true };
        ok.Click += (_, _) =>
        {
            setup.Current = machines.SelectedItem as string ?? setup.Machines.FirstOrDefault() ?? "";
            setup.Inch = inch.IsChecked == true;
            setup.RotarySubmicron = submicron.IsChecked == true;
            setup.ValidDetail = valid.IsChecked == true;
            window.DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "Machine Name", Margin = new Thickness(12, 12, 12, 0) });
        form.Children.Add(machines);
        form.Children.Add(inch);
        form.Children.Add(submicron);
        form.Children.Add(valid);
        form.Children.Add(detail);
        form.Children.Add(add);
        form.Children.Add(delete);
        root.Children.Add(form);
        window.Content = root;
        return window.ShowDialog() == true;
    }

    public static bool EditOptions(Window owner, ViewerSettings settings)
    {
        var window = new Window
        {
            Title = "Option",
            Width = 520,
            Height = 220,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var info = new CheckBox { Content = "Insert Program Information", IsChecked = settings.InsertProgramInfo, Margin = new Thickness(12, 12, 12, 4) };
        var binary = new CheckBox { Content = "Add the binary line", IsChecked = settings.AddBinaryLine, Margin = new Thickness(12, 4, 12, 8) };
        var path = new TextBox { Text = settings.DataPath, Margin = new Thickness(12, 4, 12, 4) };
        var buttons = DialogButtons(out var ok);
        ok.Click += (_, _) =>
        {
            settings.InsertProgramInfo = info.IsChecked == true;
            settings.AddBinaryLine = binary.IsChecked == true;
            if (!string.IsNullOrWhiteSpace(path.Text))
            {
                settings.DataPath = path.Text.Trim();
            }

            window.DialogResult = true;
        };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var form = new StackPanel();
        form.Children.Add(info);
        form.Children.Add(binary);
        form.Children.Add(new TextBlock { Text = "Data folder", Margin = new Thickness(12, 8, 12, 0) });
        form.Children.Add(path);
        root.Children.Add(form);
        window.Content = root;
        return window.ShowDialog() == true;
    }

    public static string? AskFind(Window owner)
    {
        var window = new Window
        {
            Title = "Find",
            Width = 380,
            Height = 150,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var box = new TextBox { Margin = new Thickness(12, 4, 12, 8) };
        var buttons = DialogButtons(out var ok);
        string? query = null;
        ok.Click += (_, _) =>
        {
            query = box.Text;
            window.DialogResult = query.Length > 0;
        };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new TextBlock { Text = "Find what", Margin = new Thickness(12, 12, 12, 0) });
        root.Children.Add(box);
        window.Content = root;
        window.Loaded += (_, _) => box.Focus();
        return window.ShowDialog() == true ? query : null;
    }

    public static void About(Window owner)
    {
        var window = new Window
        {
            Title = "About MazatrolViewerAero",
            Width = 640,
            Height = 520,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var ok = new Button { Content = "OK", Width = 88, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        ok.Click += (_, _) => window.DialogResult = true;
        var body = new DockPanel { Margin = new Thickness(16, 16, 16, 0) };
        var title = new TextBlock { Text = "MazatrolViewerAero", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(title, Dock.Top);
        body.Children.Add(title);
        body.Children.Add(new TextBox
        {
            Text = MitLicense,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
            IsTabStop = true
        });
        var root = new DockPanel();
        DockPanel.SetDock(ok, Dock.Bottom);
        root.Children.Add(ok);
        root.Children.Add(body);
        window.Content = root;
        window.ShowDialog();
    }

    private const string MitLicense =
        """
        MIT License

        Copyright (c) 2026 Abdul Hakim Mohd Hisham

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    private static void EditDetail(Window owner, FamilySetup setup)
    {
        var window = new Window
        {
            Title = "Detail Spec.",
            Width = 760,
            Height = 560,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var r392 = BitBox("R392", R392Labels, setup.R392);
        var r393 = BitBox("R393", R393Labels, setup.R393);
        var r394 = BitBox("R394", R394Labels, setup.R394);
        var row = new UniformGrid { Columns = 3, Margin = new Thickness(8) };
        row.Children.Add(r392.Panel);
        row.Children.Add(r393.Panel);
        row.Children.Add(r394.Panel);
        var buttons = DialogButtons(out var ok);
        ok.Click += (_, _) =>
        {
            setup.R392 = r392.Value();
            setup.R393 = r393.Value();
            setup.R394 = r394.Value();
            window.DialogResult = true;
        };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = row, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = root;
        window.ShowDialog();
    }

    private static (GroupBox Panel, Func<int> Value) BitBox(string title, string[] labels, int value)
    {
        var panel = new StackPanel();
        var boxes = new CheckBox[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            boxes[i] = new CheckBox { Content = labels[i], IsChecked = (value & (1 << i)) != 0, Margin = new Thickness(4, 2, 4, 2) };
            panel.Children.Add(boxes[i]);
        }

        var group = new GroupBox { Header = title, Content = panel, Margin = new Thickness(6) };
        return (group, () =>
        {
            int bits = 0;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (boxes[i].IsChecked == true)
                {
                    bits |= 1 << i;
                }
            }

            return bits;
        });
    }

    private static string? AskName(Window owner, string suggested)
    {
        var window = new Window
        {
            Title = "Registed Machine Type",
            Width = 360,
            Height = 150,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var box = new TextBox { Text = suggested, Margin = new Thickness(12, 4, 12, 8) };
        var buttons = DialogButtons(out var ok);
        string? name = null;
        ok.Click += (_, _) =>
        {
            name = box.Text.Trim();
            window.DialogResult = name.Length > 0;
        };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new TextBlock { Text = "Machine Name", Margin = new Thickness(12, 12, 12, 0) });
        root.Children.Add(box);
        window.Content = root;
        return window.ShowDialog() == true ? name : null;
    }

    private static StackPanel DialogButtons(out Button ok)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        ok = new Button { Content = "OK", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 88, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        return buttons;
    }

    private static readonly string[] R393Labels =
    [
        "0:INTE-III",
        "1:Low part Turret (INTE-III)",
        "2:INTE30",
        "3:SQR Mill-Tech",
        "4:SQR",
        "5:RIO-1",
        "6:RIO-2",
        "7:RIO-4",
        "8:INTE200",
        "9:Gantry",
        "A:INTE200SY 2 Spindle 1 Turret",
        "B:Sub 1/1000 deg",
        "C:Sub C-Axis",
        "D:IVS",
        "E:Turret B-Axis",
        "F:Output Pattern of C-Axis cutting"
    ];

    private static readonly string[] R394Labels =
    [
        "0:Builtin C-Axis",
        "1:MPX",
        "2:Sub Spindle",
        "3:C-Axis",
        "4:1 deg Orient",
        "5:Suspected Y-Axis",
        "6:Pair Direction Turret",
        "7:V/H ATC",
        "8:Spare Magezine ATC",
        "9:Vertical ATC Lathe",
        "A:1 Amplifier 2 Mortor",
        "B:Low part Graphic",
        "C:Vertical Turret Lathe",
        "D:QTX",
        "E:HD2 Main Spindle",
        "F:Stand Simulator"
    ];

    private static readonly string[] R392Labels =
    [
        "0:NC FuReDoMe (INTE-III)",
        "1:N/A",
        "2:N/A",
        "3:N/A",
        "4:N/A",
        "5:N/A",
        "6:N/A",
        "7:N/A"
    ];
}
