
using Godot;

namespace BehaviourStudio;

public static class Ux
{
    public static readonly Color Base = Color.FromHtml("151515");
    public static readonly Color Surface = Color.FromHtml("181818");
    public static readonly Color Card = Color.FromHtml("222222");
    public static readonly Color CardHover = Color.FromHtml("2A2A2A");
    public static readonly Color CardPressed = Color.FromHtml("111111");
    public static readonly Color Border = Color.FromHtml("333333");
    public static readonly Color BorderSoft = Color.FromHtml("2B2B2B");
    public static readonly Color Accent = Color.FromHtml("0070E0");
    public static readonly Color AccentHover = Color.FromHtml("0A84F0");
    public static readonly Color AccentPressed = Color.FromHtml("005CB8");

    public static readonly Color TextTitle = Color.FromHtml("EEEEEE");
    public static readonly Color TextMuted = Color.FromHtml("888888");
    public static readonly Color TextMeta = Color.FromHtml("A0A0A0");
    public static readonly Color TextCode = Color.FromHtml("00A0DA");
    public static readonly Color TextDisabled = Color.FromHtml("5A5A5A");

    public static float Scale = 1.0f;

    public static int Px(float value) => Mathf.RoundToInt(value * Scale);

    public static Font BoldFont() =>
        ThemeDB.FallbackFont;

    public static Texture2D EditorIcon(string name) =>
        null!;

    public static StyleBoxFlat Fill(Color bg, Color border, int borderWidth = 1, int radius = 4)
    {
        var style = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        style.SetBorderWidthAll(Px(borderWidth));
        style.SetCornerRadiusAll(Px(radius));
        return style;
    }

    public static StyleBoxFlat Padded(Color bg, Color border, int borderWidth, int radius, int padding)
    {
        var style = Fill(bg, border, borderWidth, radius);
        style.ContentMarginLeft = Px(padding);
        style.ContentMarginRight = Px(padding);
        style.ContentMarginTop = Px(padding * 0.6f);
        style.ContentMarginBottom = Px(padding * 0.6f);
        return style;
    }

    public static PanelContainer CardPanel(int padding = 12)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Padded(Card, Border, 1, 4, padding));
        return panel;
    }

    public static Label SectionTitle(string text)
    {
        var label = new Label { Text = text.ToUpperInvariant() };
        label.AddThemeColorOverride("font_color", TextMuted);
        label.AddThemeFontOverride("font", BoldFont());
        label.AddThemeFontSizeOverride("font_size", Px(10));
        return label;
    }

    public static Label FieldLabel(string text)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(Px(150), 0) };
        label.AddThemeColorOverride("font_color", TextMeta);
        label.AddThemeFontSizeOverride("font_size", Px(12));
        return label;
    }

    // A status pill: bordered, rounded, its own background, so a long status line reads as one
    // compact tag instead of loose text competing with the labels around it.
    public static Label StatusPill(string text = "")
    {
        var pill = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        pill.AddThemeColorOverride("font_color", TextMeta);
        pill.AddThemeFontSizeOverride("font_size", Px(11));
        pill.AddThemeStyleboxOverride("normal", Padded(Surface, BorderSoft, 1, 10, 10));
        return pill;
    }

    public static Button PrimaryButton(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(Px(180), Px(30)) };
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", TextDisabled);
        button.AddThemeFontOverride("font", BoldFont());
        button.AddThemeFontSizeOverride("font_size", Px(12));
        button.AddThemeStyleboxOverride("normal", Fill(Accent, Accent, 1, 4));
        button.AddThemeStyleboxOverride("hover", Fill(AccentHover, AccentHover, 1, 4));
        button.AddThemeStyleboxOverride("pressed", Fill(AccentPressed, AccentPressed, 1, 4));
        button.AddThemeStyleboxOverride("disabled", Fill(Card, Border, 1, 4));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    public static Button SecondaryButton(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, Px(28)) };
        button.AddThemeColorOverride("font_color", TextTitle);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", TextDisabled);
        button.AddThemeFontSizeOverride("font_size", Px(12));
        button.AddThemeStyleboxOverride("normal", Fill(Card, Border, 1, 4));
        button.AddThemeStyleboxOverride("hover", Fill(CardHover, Accent, 1, 4));
        button.AddThemeStyleboxOverride("pressed", Fill(CardPressed, Accent, 1, 4));
        button.AddThemeStyleboxOverride("disabled", Fill(Surface, BorderSoft, 1, 4));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    public static LineEdit Field(string placeholder = "", string? rightIcon = null)
    {
        var field = new LineEdit
        {
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, Px(28)),
        };
        if (rightIcon != null) field.RightIcon = EditorIcon(rightIcon);
        field.AddThemeColorOverride("font_color", TextTitle);
        field.AddThemeColorOverride("font_placeholder_color", TextDisabled);
        field.AddThemeFontSizeOverride("font_size", Px(12));
        field.AddThemeStyleboxOverride("normal", Padded(Surface, Border, 1, 4, 8));
        field.AddThemeStyleboxOverride("focus", Padded(Surface, Accent, 1, 4, 8));
        return field;
    }

    public static void StyleGrid(Tree tree)
    {
        tree.AddThemeStyleboxOverride("panel", Fill(Surface, Border, 1, 4));
        tree.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        tree.AddThemeStyleboxOverride("selected", Fill(Accent.Darkened(0.35f), Accent, 1, 0));
        tree.AddThemeStyleboxOverride("selected_focus", Fill(Accent.Darkened(0.2f), Accent, 1, 0));
        tree.AddThemeStyleboxOverride("hovered", Fill(CardHover, CardHover, 0, 0));
        tree.AddThemeStyleboxOverride("title_button_normal", Padded(Card, BorderSoft, 1, 0, 6));
        tree.AddThemeStyleboxOverride("title_button_hover", Padded(CardHover, BorderSoft, 1, 0, 6));
        tree.AddThemeStyleboxOverride("title_button_pressed", Padded(CardPressed, BorderSoft, 1, 0, 6));
        tree.AddThemeColorOverride("title_button_color", TextMuted);
        tree.AddThemeColorOverride("font_color", TextTitle);
        tree.AddThemeColorOverride("font_selected_color", Colors.White);
        tree.AddThemeFontSizeOverride("font_size", Px(12));
        tree.AddThemeConstantOverride("v_separation", Px(6));
        tree.AddThemeConstantOverride("inner_item_margin_left", Px(6));
        tree.AddThemeConstantOverride("inner_item_margin_right", Px(6));
    }

    public static void StyleProgressBar(ProgressBar bar)
    {
        bar.AddThemeStyleboxOverride("background", Fill(CardPressed, Border, 1, 3));
        bar.AddThemeStyleboxOverride("fill", Fill(Accent, Accent, 0, 3));
        bar.AddThemeColorOverride("font_color", TextTitle);
        bar.AddThemeFontSizeOverride("font_size", Px(10));
    }

    public static HSeparator Divider()
    {
        var separator = new HSeparator();
        var style = new StyleBoxFlat { BgColor = BorderSoft };
        style.ContentMarginTop = Px(1);
        separator.AddThemeStyleboxOverride("separator", style);
        return separator;
    }
}

