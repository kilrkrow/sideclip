using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Sideclip.History;
using Sideclip.Picker;
using Sideclip.TypeOut;

namespace Sideclip.Tests;

public sealed class PickerNoArmHelpTests
{
    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static object? Call(object instance, string name, params object?[] args) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);

    private static string SourceRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory.Parent is not null && !File.Exists(Path.Combine(directory.FullName, "App.xaml.cs")))
                directory = directory.Parent;
            if (!File.Exists(Path.Combine(directory.FullName, "App.xaml.cs")))
                throw new DirectoryNotFoundException("Sideclip source root not found from " + AppContext.BaseDirectory);
            return directory.FullName;
        }
    }

    private static string ReadPickerCs() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Picker", "PickerWindow.xaml.cs"));

    private static string ReadPickerXaml() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Picker", "PickerWindow.xaml"));

    private static string ReadHelpCs() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Picker", "PickerNoArmHelp.cs"));

    [Fact]
    public void Help_lines_are_ascii_and_omit_Arm_Go_paste_CtrlV()
    {
        foreach (var line in new[]
        {
            PickerNoArmHelp.NavigationLine,
            PickerNoArmHelp.CopyLine,
            PickerNoArmHelp.FormatTypeOutLine(false, null),
            PickerNoArmHelp.FormatTypeOutLine(true, null),
            PickerNoArmHelp.FormatTypeOutLine(true, ""),
            PickerNoArmHelp.FormatTypeOutLine(true, "Ctrl+Win+Shift+T"),
            PickerNoArmHelp.ComposeCompactHelp(true, "Ctrl+Win+Shift+T")
        })
        {
            Assert.True(PickerNoArmHelp.IsAscii(line), line);
            Assert.DoesNotContain("\u2026", line);
            Assert.DoesNotContain("Arm", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Go hotkey", line, StringComparison.OrdinalIgnoreCase);
            if (!line.Contains("no Ctrl+V", StringComparison.Ordinal))
                Assert.DoesNotContain("Ctrl+V", line, StringComparison.Ordinal);
            Assert.DoesNotContain("paste", line, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("no Ctrl+V", PickerNoArmHelp.CopyLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Type_out_help_states_disabled_unassigned_unavailable_and_configured()
    {
        Assert.Equal("Text-only type out: disabled.", PickerNoArmHelp.FormatTypeOutLine(false, "Ctrl+Win+Shift+T"));
        Assert.Equal("Text-only type out: disabled.", PickerNoArmHelp.FormatTypeOutLine(false, null));
        Assert.Equal("Text-only type out: unavailable.", PickerNoArmHelp.FormatTypeOutLine(true, null));
        Assert.Equal("Text-only type out: unassigned.", PickerNoArmHelp.FormatTypeOutLine(true, ""));
        Assert.Equal("Text-only type out: unassigned.", PickerNoArmHelp.FormatTypeOutLine(true, "   "));
        Assert.Equal(
            "Text-only type out: Ctrl+Alt+F8. Select text, focus destination, then press shortcut.",
            PickerNoArmHelp.FormatTypeOutLine(true, " Ctrl+Alt+F8 "));
    }

    [Fact]
    public void Source_audit_every_advertised_navigation_and_copy_key_matches_handlers()
    {
        var cs = ReadPickerCs();
        var help = ReadHelpCs();

        Assert.Contains("MoveSel(1)", cs);
        Assert.Contains("MoveSel(-1)", cs);
        Assert.Contains("MoveSel(6)", cs);
        Assert.Contains("MoveSel(-6)", cs);
        Assert.Contains("Key.Home", cs);
        Assert.Contains("Key.End", cs);
        Assert.Contains("Key.Back", cs);
        Assert.Contains("Key.Escape", cs);
        Assert.Contains("EscapePicker()", cs);
        Assert.Contains("if (_filter.Length > 0)", cs);
        Assert.Contains("PickerCommitAction.Copy", cs);
        Assert.Contains("_commit(sel, false)", cs);
        Assert.Contains("Commit(paste: false)", cs);
        Assert.DoesNotContain("Commit(paste: true)", cs);
        Assert.DoesNotContain("PickerCommitAction.Paste", cs);
        Assert.DoesNotContain("ArmTypeOut", cs);
        Assert.DoesNotContain("InputSender.CtrlV", cs);
        Assert.DoesNotContain("FocusWindow", cs);

        Assert.Contains("Up/Down one row (wrap)", help);
        Assert.Contains("PageUp/PageDown six (wrap)", help);
        Assert.Contains("Home/End", help);
        Assert.Contains("Backspace", help);
        Assert.Contains("Esc clears filter, then closes", help);
        Assert.Contains("Enter, Shift+Enter, double-click, or Copy", help);
        Assert.Contains("clipboard only; no Ctrl+V", help);

        Assert.Equal(PickerCommitAction.Copy, PickerKeyMapping.Resolve(Key.Enter, ModifierKeys.None, new ClipEntry("a", false, "t"), true));
        Assert.Equal(PickerCommitAction.Copy, PickerKeyMapping.Resolve(Key.Enter, ModifierKeys.Shift, new ClipEntry("a", false, "t"), true));
        Assert.Equal(PickerCommitAction.None, PickerKeyMapping.Resolve(Key.T, ModifierKeys.Control, new ClipEntry("a", false, "t"), true));
    }

    [Fact]
    public void Xaml_footer_exposes_visible_help_runs_version_and_dark_copy_button()
    {
        var xaml = ReadPickerXaml();
        Assert.Contains("Sideclip 0.3.0", xaml);
        Assert.Contains("x:Name=\"TypeOutReason\"", xaml);
        Assert.Contains("x:Name=\"NavHelp\"", xaml);
        Assert.Contains("x:Name=\"CopyHelp\"", xaml);
        Assert.Contains("x:Name=\"TypeOutHelp\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Sideclip picker status and keyboard help\"", xaml);
        Assert.Contains("x:Name=\"CopyButton\"", xaml);
        Assert.Matches("x:Name=\"CopyButton\"[^>]*Style=\"\\{StaticResource PickerButton\\}\"", xaml);
        Assert.DoesNotContain("TypeOutButton", xaml);
        Assert.DoesNotContain("Arm", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\u2026", xaml);
        Assert.Contains("Property=\"Foreground\" Value=\"#9AA0AC\"", xaml);
        Assert.Contains("Property=\"Background\" Value=\"#262A35\"", xaml);
    }

    [Fact]
    public void Live_help_shows_configured_shortcut_and_updates_after_settings()
    {
        Sta(() =>
        {
            var entry = new ClipEntry("alpha\tbeta", false, "test");
            var picker = new PickerWindow(new[] { entry }, IntPtr.Zero, (_, _) => { }, true, "Ctrl+Win+Shift+T");
            var nav = (Run)picker.FindName("NavHelp")!;
            var copy = (Run)picker.FindName("CopyHelp")!;
            var typeOut = (Run)picker.FindName("TypeOutHelp")!;
            var reason = (Run)picker.FindName("TypeOutReason")!;

            Assert.Equal(PickerNoArmHelp.NavigationLine, nav.Text);
            Assert.Equal(PickerNoArmHelp.CopyLine, copy.Text);
            Assert.Contains("Ctrl+Win+Shift+T", typeOut.Text, StringComparison.Ordinal);
            Assert.Contains("Eligible.", reason.Text, StringComparison.Ordinal);
            Assert.Contains(TypeOutText.TargetKeyWarning, reason.Text, StringComparison.Ordinal);

            picker.UpdateTypeOutHelp(false, "Ctrl+Win+Shift+T");
            Assert.Equal("Text-only type out: disabled.", typeOut.Text);
            Assert.Equal("Type-out is off.", reason.Text);

            picker.UpdateTypeOutHelp(true, "");
            Assert.Equal("Text-only type out: unassigned.", typeOut.Text);

            picker.UpdateTypeOutHelp(true, null);
            Assert.Equal("Text-only type out: unavailable.", typeOut.Text);

            picker.UpdateTypeOutHelp(true, "Ctrl+Alt+F8");
            Assert.Contains("Ctrl+Alt+F8", typeOut.Text, StringComparison.Ordinal);
            Assert.Contains("Eligible.", reason.Text, StringComparison.Ordinal);

            picker.Close();
        });
    }

    [Fact]
    public void Programmatic_initial_and_filter_selection_do_not_prime_candidate()
    {
        Sta(() =>
        {
            var first = new ClipEntry("first", false, "test");
            var second = new ClipEntry("second", false, "test");
            var picker = new PickerWindow(new[] { first, second }, IntPtr.Zero, (_, _) => { }, true, "Ctrl+Win+Shift+T");
            var choices = new List<ClipEntry>();
            var invalidations = 0;
            picker.CandidateSelected += choices.Add;
            picker.CandidateInvalidated += () => invalidations++;

            var list = (ListBox)picker.FindName("List")!;
            Assert.Equal(0, list.SelectedIndex);
            Assert.Empty(choices);

            list.SelectedIndex = 1;
            Call(picker, "ApplyFilter");
            Assert.Empty(choices);
            Assert.Equal(0, invalidations);

            Call(picker, "MoveSel", 1);
            Assert.Same(second, Assert.Single(choices));

            typeof(PickerWindow).GetField("_filter", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(picker, "no-match-zzzz");
            Call(picker, "ApplyFilter");
            Assert.Equal(1, invalidations);

            picker.Close();
        });
    }

    [Fact]
    public void Deliberate_nav_click_and_image_selection_contract()
    {
        Sta(() =>
        {
            var text = new ClipEntry("text-one", false, "test");
            var image = new ClipEntry("", false, "test", new byte[] { 137, 80, 78, 71 });
            var picker = new PickerWindow(new[] { text, image }, IntPtr.Zero, (_, _) => { }, true, "Ctrl+Win+Shift+T");
            var choices = new List<ClipEntry>();
            picker.CandidateSelected += choices.Add;
            var list = (ListBox)picker.FindName("List")!;

            Call(picker, "OnRowChoose", new ListBoxItem { Content = list.Items[0] }, null);
            Assert.Same(text, Assert.Single(choices));

            Call(picker, "OnRowChoose", new ListBoxItem { Content = list.Items[1] }, null);
            Assert.Equal(2, choices.Count);
            Assert.Same(image, choices[1]);

            // Click preview synchronizes the row used by immediate Copy/Enter.
            Assert.Equal(1, list.SelectedIndex);
            Call(picker, "MoveSel", -1);
            Assert.Equal(3, choices.Count);
            Assert.Same(text, choices[2]);
            Assert.Equal(0, list.SelectedIndex);

            picker.Close();
        });
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("ShiftEnter")]
    [InlineData("DoubleClick")]
    [InlineData("Copy")]
    [InlineData("PreviewDoubleClick")]
    public void Copy_only_keyboard_and_mouse_paths_never_request_paste(string action)
    {
        Sta(() =>
        {
            var entry = new ClipEntry("copy-me", false, "test");
            var commits = new List<(ClipEntry Entry, bool Paste)>();
            var choices = new List<ClipEntry>();
            var picker = new PickerWindow(new[] { entry }, IntPtr.Zero, (row, paste) => commits.Add((row, paste)), true, "Ctrl+Win+Shift+T");
            picker.CandidateSelected += choices.Add;

            if (action is "Enter" or "ShiftEnter")
            {
                Assert.Equal(PickerCommitAction.Copy, PickerKeyMapping.Resolve(
                    Key.Enter,
                    action == "Enter" ? ModifierKeys.None : ModifierKeys.Shift,
                    entry,
                    true));
                Call(picker, "Commit", false);
            }
            else if (action == "DoubleClick")
            {
                Call(picker, "OnRowActivate", null,
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = Control.MouseDoubleClickEvent
                    });
            }
            else if (action == "PreviewDoubleClick")
            {
                Assert.Contains("OnPreviewActivate", ReadPickerCs(), StringComparison.Ordinal);
                Assert.Contains("if (e.ClickCount < 2)", ReadPickerCs(), StringComparison.Ordinal);
                Call(picker, "Commit", false);
            }
            else
            {
                Call(picker, "OnCopyClick", null, new System.Windows.RoutedEventArgs());
            }

            Assert.Same(entry, Assert.Single(choices));
            Assert.Equal((entry, false), Assert.Single(commits));
        });
    }

    [Fact]
    public void Esc_filter_first_then_invalidate_and_scrim_preserves_candidate_cancel_for_CLI()
    {
        Sta(() =>
        {
            var entry = new ClipEntry("alpha", false, "test");
            var picker = new PickerWindow(new[] { entry }, IntPtr.Zero, (_, _) => Assert.Fail("no commit"), true, "Ctrl+Win+Shift+T");
            var choices = new List<ClipEntry>();
            var invalidations = 0;
            var cancellations = 0;
            picker.CandidateSelected += choices.Add;
            picker.CandidateInvalidated += () => invalidations++;
            picker.Cancelled += () => cancellations++;

            Call(picker, "MoveSel", 0);
            Assert.Same(entry, Assert.Single(choices));

            typeof(PickerWindow).GetField("_filter", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(picker, "alp");
            Call(picker, "ApplyFilter");
            picker.EscapePicker();
            Assert.Equal(0, invalidations);
            Assert.Equal(0, cancellations);

            var closed = new PickerWindow(new[] { entry }, IntPtr.Zero, (_, _) => { }, true, null);
            var closedInvalidations = 0;
            var closedCancels = 0;
            closed.CandidateSelected += _ => { };
            closed.CandidateInvalidated += () => closedInvalidations++;
            closed.Cancelled += () => closedCancels++;
            Call(closed, "MoveSel", 0);
            closed.Close();
            Assert.Equal(0, closedInvalidations);
            Assert.Equal(1, closedCancels);

            picker.EscapePicker();
            Assert.Equal(1, invalidations);
        });
    }

    [Fact]
    public void Page_navigation_wraps_six_rows_and_help_advertises_it()
    {
        Sta(() =>
        {
            var entries = Enumerable.Range(0, 10).Select(i => new ClipEntry("row-" + i, false, "test")).ToArray();
            var picker = new PickerWindow(entries, IntPtr.Zero, (_, _) => { }, true, "Ctrl+Win+Shift+T");
            var choices = new List<ClipEntry>();
            picker.CandidateSelected += choices.Add;
            var list = (ListBox)picker.FindName("List")!;

            Assert.Equal(0, list.SelectedIndex);
            Call(picker, "MoveSel", 6);
            Assert.Equal(6, list.SelectedIndex);
            Assert.Same(entries[6], choices[^1]);

            Call(picker, "MoveSel", 6);
            Assert.Equal(2, list.SelectedIndex);
            Assert.Same(entries[2], choices[^1]);

            Call(picker, "MoveSel", -6);
            Assert.Equal(6, list.SelectedIndex);

            Assert.Contains("PageUp/PageDown six (wrap)", ((Run)picker.FindName("NavHelp")!).Text, StringComparison.Ordinal);
            picker.Close();
        });
    }

    [Fact]
    public void Public_events_and_UpdateTypeOutHelp_remain_on_picker_surface()
    {
        var type = typeof(PickerWindow);
        Assert.NotNull(type.GetEvent("CandidateSelected"));
        Assert.NotNull(type.GetEvent("CandidateInvalidated"));
        Assert.NotNull(type.GetEvent("Deleted"));
        Assert.NotNull(type.GetEvent("Cleared"));
        Assert.NotNull(type.GetEvent("Cancelled"));
        Assert.NotNull(type.GetMethod("UpdateTypeOutHelp", BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(type.GetMethod("EscapePicker", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
    }
    [Fact]
    public void Duplicate_row_activation_paths_commit_once_and_copy_the_clicked_row()
    {
        Sta(() =>
        {
            var first = new ClipEntry("first", false, "test");
            var clicked = new ClipEntry("clicked", false, "test");
            var commits = new List<ClipEntry>();
            var picker = new PickerWindow(new[] { first, clicked }, IntPtr.Zero,
                (entry, paste) => { Assert.False(paste); commits.Add(entry); }, true, "Ctrl+Win+Shift+T");
            var list = (ListBox)picker.FindName("List")!;
            Call(picker, "OnRowChoose", new ListBoxItem { Content = list.Items[1] }, null);
            Assert.Equal(1, list.SelectedIndex);
            Call(picker, "Commit", false);
            Call(picker, "Commit", false);
            Assert.Same(clicked, Assert.Single(commits));
        });
    }
    [Fact]
    public void Footer_accessible_name_exposes_current_visible_shortcut_not_a_generic_label()
    {
        Sta(() =>
        {
            var picker = new PickerWindow(Array.Empty<ClipEntry>(), IntPtr.Zero, (_, _) => { }, true, "Ctrl+Alt+F9");
            var footer = (TextBlock)picker.FindName("FooterHelp")!;
            picker.UpdateTypeOutHelp(true, "Ctrl+Alt+F9");
            Assert.Contains("Ctrl+Alt+F9", System.Windows.Automation.AutomationProperties.GetName(footer));
            picker.UpdateTypeOutHelp(true, "");
            Assert.Contains("unassigned", System.Windows.Automation.AutomationProperties.GetName(footer));
            picker.Close();
        });
    }
}
