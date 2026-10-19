using System.Reflection;
using System.Windows.Forms;
using Sideclip.Settings;
using Xunit;

namespace Sideclip.Tests;

public class SettingsFormTests
{
    private static void Sta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { body(); } catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null)
            throw new Exception("STA body failed: " + error.Message, error);
    }

    private static void ClickOk(SettingsForm f) =>
        typeof(SettingsForm).GetMethod("OnOkClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(f, new object?[] { null, EventArgs.Empty });

    private static TextBox Num(SettingsForm f, string name) => (TextBox)f.Controls.Find(name, true).Single();
    private static CheckBox Chk(SettingsForm f, string name) => (CheckBox)f.Controls.Find(name, true).Single();

    private static AppSettings Caps(int text, int images) => new() { MaxTextClips = text, MaxImageClips = images };

    [Fact]
    public void Lowering_asks_with_the_exact_count_and_type_and_decline_restores_the_old_value()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 40, () => 10);
            string? asked = null;
            f.ConfirmRemoval = (_, m) => { asked = m; return false; };
            Num(f, "Text clips limit").Text = "30";

            ClickOk(f);

            Assert.NotNull(asked);
            Assert.Contains("10 text clips (limit 30)", asked);
            Assert.DoesNotContain("image", asked);
            Assert.Equal(DialogResult.None, f.DialogResult);
            Assert.Equal(100, f.MaxTextClips);
            Assert.Equal("100", Num(f, "Text clips limit").Text);
            Assert.Equal(0, f.ConfirmedTextRemovals);
            Assert.Equal(0, f.ConfirmedImageRemovals);
        });
    }

    [Fact]
    public void Confirming_returns_ok_with_the_confirmed_counts()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 40, () => 10);
            f.ConfirmRemoval = (_, _) => true;
            Num(f, "Text clips limit").Text = "30";
            Num(f, "Image clips limit").Text = "4";

            ClickOk(f);

            Assert.Equal(DialogResult.OK, f.DialogResult);
            Assert.Equal(30, f.MaxTextClips);
            Assert.Equal(4, f.MaxImageClips);
            Assert.Equal(10, f.ConfirmedTextRemovals);
            Assert.Equal(6, f.ConfirmedImageRemovals);
        });
    }

    [Fact]
    public void The_count_in_the_confirmation_is_read_when_OK_is_pressed_not_when_the_window_opened()
    {
        Sta(() =>
        {
            var stored = 40;
            using var f = new SettingsForm(Caps(100, 25), () => stored, () => 0);
            string? asked = null;
            f.ConfirmRemoval = (_, m) => { asked = m; return true; };
            stored = 60; // clips arrived while the window was open
            Num(f, "Text clips limit").Text = "30";

            ClickOk(f);

            Assert.Contains("30 text clips (limit 30)", asked);
            Assert.Equal(30, f.ConfirmedTextRemovals);
        });
    }

    [Fact]
    public void Message_names_both_types_when_both_are_lowered()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 40, () => 10);
            string? asked = null;
            f.ConfirmRemoval = (_, m) => { asked = m; return false; };
            Num(f, "Text clips limit").Text = "39";
            Num(f, "Image clips limit").Text = "9";
            ClickOk(f);
            Assert.Contains("1 text clip (limit 39)", asked);
            Assert.Contains("1 image clip (limit 9)", asked);
        });
    }

    [Fact]
    public void Raising_a_cap_or_choosing_unlimited_does_not_ask_and_removes_nothing()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 400, () => 400);
            var asks = 0;
            f.ConfirmRemoval = (_, _) => { asks++; return false; };
            Num(f, "Text clips limit").Text = "500";
            Chk(f, "Unlimited image clips").Checked = true;

            ClickOk(f);

            Assert.Equal(0, asks);
            Assert.Equal(DialogResult.OK, f.DialogResult);
            Assert.Equal(500, f.MaxTextClips);
            Assert.Equal(AppSettings.Unlimited, f.MaxImageClips);
            Assert.False(Num(f, "Image clips limit").Enabled);
            Assert.Equal(0, f.ConfirmedTextRemovals + f.ConfirmedImageRemovals);
        });
    }

    [Fact]
    public void Unlimited_to_a_number_below_what_is_stored_is_a_lowering()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(AppSettings.Unlimited, AppSettings.Unlimited), () => 5, () => 25);
            string? asked = null;
            f.ConfirmRemoval = (_, m) => { asked = m; return true; };
            Chk(f, "Unlimited image clips").Checked = false;
            Num(f, "Image clips limit").Text = "10";
            ClickOk(f);
            Assert.Contains("15 image clips (limit 10)", asked);
            Assert.Equal(15, f.ConfirmedImageRemovals);
            Assert.Equal(0, f.ConfirmedTextRemovals);
        });
    }

    [Fact]
    public void Lowering_a_cap_that_is_still_above_whats_stored_does_not_ask()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 12, () => 3);
            var asks = 0;
            f.ConfirmRemoval = (_, _) => { asks++; return false; };
            Num(f, "Text clips limit").Text = "20";
            ClickOk(f);
            Assert.Equal(0, asks);
            Assert.Equal(DialogResult.OK, f.DialogResult);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    public void An_empty_or_zero_limit_is_refused_and_the_old_value_comes_back(string typed)
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 400, () => 400);
            var asks = 0;
            f.ConfirmRemoval = (_, _) => { asks++; return true; };
            Num(f, "Text clips limit").Text = typed;

            ClickOk(f);

            Assert.Equal(0, asks);
            Assert.Equal(DialogResult.None, f.DialogResult);
            Assert.Equal("100", Num(f, "Text clips limit").Text);
            Assert.Equal(0, f.ConfirmedTextRemovals + f.ConfirmedImageRemovals);
        });
    }

    [Fact]
    public void Letters_and_symbols_are_stripped_from_a_limit_box()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 5, () => 5);
            Num(f, "Text clips limit").Text = "1a2-3";
            Assert.Equal("123", Num(f, "Text clips limit").Text);
        });
    }

    [Fact]
    public void A_limit_above_the_maximum_is_clamped()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 5, () => 5);
            Num(f, "Image clips limit").Text = "99999";
            ClickOk(f);
            Assert.Equal(AppSettings.MaxImageCap, f.MaxImageClips);
        });
    }

    [Fact]
    public void Cancel_button_cancels_and_applies_nothing()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 40, () => 10);
            Assert.Equal(DialogResult.Cancel, ((Button)f.CancelButton!).DialogResult);
        });
    }

    [Fact]
    public void All_visible_text_is_ascii()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 40, () => 10);
            var strings = new List<string> { f.Text, SettingsForm.BuildRemovalMessage(3, 5, 1, 2), SettingsForm.StoredSummary(1, 1) };
            void Walk(Control c)
            {
                strings.Add(c.Text);
                strings.Add(c.AccessibleName ?? string.Empty);
                foreach (Control k in c.Controls) Walk(k);
            }
            Walk(f);
            foreach (var s in strings)
                Assert.All(s, ch => Assert.True(ch < 128, "non-ASCII char in UI string: " + s));
        });
    }

    [Fact]
    public void Controls_are_at_least_24px_high_with_gaps_and_margins()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(100, 25), () => 40, () => 10);
            var interactive = new List<Control>();
            void Walk(Control c)
            {
                if (c is Button or CheckBox or TextBox) interactive.Add(c);
                foreach (Control k in c.Controls) Walk(k);
            }
            Walk(f);
            Assert.True(interactive.Count >= 8);
            foreach (var c in interactive)
            {
                Assert.True(c.Height >= 24, c.Name + " (" + c.GetType().Name + ") height " + c.Height);
                Assert.True(c.Left >= 8 && c.Top >= 8, c.Name + " too near the top/left edge");
                Assert.True(f.ClientSize.Width - c.Right >= 8 && f.ClientSize.Height - c.Bottom >= 8, c.Name + " too near the right/bottom edge");
            }

            foreach (var a in f.Controls.Cast<Control>())
            foreach (var b in f.Controls.Cast<Control>())
            {
                if (ReferenceEquals(a, b)) continue;
                var gapX = Math.Max(b.Left - a.Right, a.Left - b.Right);
                var gapY = Math.Max(b.Top - a.Bottom, a.Top - b.Bottom);
                Assert.True(Math.Max(gapX, gapY) >= 6, a.Text + " / " + b.Text + " closer than 6px (" + gapX + "," + gapY + ")");
            }
        });
    }

    [Fact]
    public void Over_limit_profile_prompts_on_OK_with_exact_counts_even_when_no_limit_changed()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(30, 5), () => 40, () => 12);
            string? asked = null;
            f.ConfirmRemoval = (_, m) => { asked = m; return true; };

            ClickOk(f);

            Assert.NotNull(asked);
            Assert.Contains("over its limits", asked);
            Assert.Contains("10 text clips (limit 30)", asked);
            Assert.Contains("7 image clips (limit 5)", asked);
            Assert.Equal(DialogResult.OK, f.DialogResult);
            Assert.Equal(10, f.ConfirmedTextRemovals);
            Assert.Equal(7, f.ConfirmedImageRemovals);
            Assert.Equal(30, f.MaxTextClips);
            Assert.Equal(5, f.MaxImageClips);
        });
    }

    [Fact]
    public void Declining_the_over_limit_prompt_removes_nothing_and_keeps_the_limits()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(30, 5), () => 40, () => 12);
            var asks = 0;
            f.ConfirmRemoval = (_, _) => { asks++; return false; };

            ClickOk(f);

            Assert.Equal(1, asks);
            Assert.Equal(0, f.ConfirmedTextRemovals);
            Assert.Equal(0, f.ConfirmedImageRemovals);
            Assert.Equal(30, f.MaxTextClips);
            Assert.Equal(5, f.MaxImageClips);
            // Limits unchanged: the dialog closes so other settings can be saved.
            Assert.Equal(DialogResult.OK, f.DialogResult);
        });
    }

    [Fact]
    public void Declining_while_a_limit_was_changed_still_restores_and_stays_open()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(30, 5), () => 40, () => 12);
            f.ConfirmRemoval = (_, _) => false;
            Num(f, "Text clips limit").Text = "20";

            ClickOk(f);

            Assert.Equal(DialogResult.None, f.DialogResult);
            Assert.Equal("30", Num(f, "Text clips limit").Text);
            Assert.Equal(0, f.ConfirmedTextRemovals + f.ConfirmedImageRemovals);
        });
    }

    [Fact]
    public void Raising_a_limit_that_is_still_below_whats_stored_asks_nothing_and_removes_nothing()
    {
        Sta(() =>
        {
            using var f = new SettingsForm(Caps(30, 5), () => 40, () => 3);
            var asks = 0;
            f.ConfirmRemoval = (_, _) => { asks++; return false; };
            Num(f, "Text clips limit").Text = "35";

            ClickOk(f);

            Assert.Equal(0, asks);
            Assert.Equal(DialogResult.OK, f.DialogResult);
            Assert.Equal(35, f.MaxTextClips);
            Assert.Equal(0, f.ConfirmedTextRemovals + f.ConfirmedImageRemovals);
        });
    }

    [Fact]
    public void Profile_at_or_under_its_limits_and_unlimited_profiles_never_prompt_on_OK()
    {
        Sta(() =>
        {
            var asks = 0;
            using var a = new SettingsForm(Caps(30, 5), () => 30, () => 5);
            a.ConfirmRemoval = (_, _) => { asks++; return false; };
            ClickOk(a);
            using var b = new SettingsForm(Caps(AppSettings.Unlimited, AppSettings.Unlimited), () => 5000, () => 900);
            b.ConfirmRemoval = (_, _) => { asks++; return false; };
            ClickOk(b);
            Assert.Equal(0, asks);
            Assert.Equal(DialogResult.OK, a.DialogResult);
            Assert.Equal(DialogResult.OK, b.DialogResult);
        });
    }

    [Fact]
    public void Over_limit_message_is_ascii()
    {
        var m = SettingsForm.BuildRemovalMessage(10, 30, 7, 5, overLimitOnly: true);
        Assert.All(m, c => Assert.True(c == '\r' || c == '\n' || (c >= ' ' && c <= '~')));
    }}
