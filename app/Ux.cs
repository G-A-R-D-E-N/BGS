using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

public static class Ux
{
    public const double FontTitle = 14;
    public const double FontBody = 13;
    public const double FontMeta = 12;
    public const double FontSmall = 11;
    public const double ControlHeight = 28;
    public const double Radius = 4;
    public const double Space = 8;
    public static readonly Color Base = Color.Parse("#151515");
    public static readonly Color Rail = Color.Parse("#101010");
    public static readonly Color Card = Color.Parse("#222222");
    public static readonly Color CardHover = Color.Parse("#2C2C2C");
    public static readonly Color Border = Color.Parse("#3A3A3A");
    public static readonly Color Accent = Color.Parse("#0070E0");
    public static readonly Color TextTitle = Color.Parse("#E6E6E6");
    public static readonly Color TextMeta = Color.Parse("#B0B0B0");
    public static readonly Color TextMuted = Color.Parse("#8C8C8C");
    public static readonly Color TextDisabled = Color.Parse("#5A5A5A");
    public static readonly Color TextCode = Color.Parse("#00A0DA");
    public static readonly Color Bad = Color.Parse("#FF5555");
    public static readonly Color Warn = Color.Parse("#E0A030");
    public static readonly Color Good = Color.Parse("#3FB950");

    public static readonly Color RouteColour = Color.Parse("#58D0C0");

    public static readonly Color Wildcard = Color.Parse("#F778BA");

    public static readonly Color Casing = Base;

    public static readonly IBrush BaseBrush = new SolidColorBrush(Base);
    public static readonly IBrush RailBrush = new SolidColorBrush(Rail);
    public static readonly IBrush CardBrush = new SolidColorBrush(Card);
    public static readonly IBrush CardHoverBrush = new SolidColorBrush(CardHover);
    public static readonly IBrush BorderBrush = new SolidColorBrush(Border);
    public static readonly IBrush TitleBrush = new SolidColorBrush(TextTitle);
    public static readonly IBrush MetaBrush = new SolidColorBrush(TextMeta);
    public static readonly IBrush MutedBrush = new SolidColorBrush(TextMuted);
    public static readonly IBrush DisabledBrush = new SolidColorBrush(TextDisabled);
    public static readonly IBrush CodeBrush = new SolidColorBrush(TextCode);
    public static readonly IBrush AccentBrush = new SolidColorBrush(Accent);
    public static readonly IBrush BadBrush = new SolidColorBrush(Bad);
    public static readonly IBrush WarnBrush = new SolidColorBrush(Warn);

    public static TextBox Field(string watermark = "", double minWidth = 0) => new()
    {
        Watermark = watermark,
        MinWidth = minWidth,
        MinHeight = ControlHeight,
        Background = CardBrush,
        Foreground = TitleBrush,
        BorderBrush = BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(Radius),
        Padding = new Thickness(8, 5),
        FontSize = FontBody,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    public static Button Primary(string text) => Style(new Button { Content = text }, AccentBrush, Brushes.White);

    public static Button Secondary(string text) => Style(new Button { Content = text }, CardBrush, MetaBrush);

    private static Button Style(Button button, IBrush background, IBrush foreground)
    {
        button.Background = background;
        button.Foreground = foreground;
        button.BorderBrush = BorderBrush;
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(Radius);
        button.Padding = new Thickness(12, 5);
        button.MinHeight = ControlHeight;
        button.FontSize = FontMeta;
        button.MinWidth = 84;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = MutedBrush,
        FontSize = FontSmall,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(1, 2, 0, 2),
    };

    public static Border Group(string title, params Control[] rows)
    {
        var body = new StackPanel { Spacing = Space };
        body.Children.Add(SectionTitle(title));
        foreach (var row in rows) body.Children.Add(row);
        return new Border
        {
            Background = CardBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius),
            Padding = new Thickness(10),
            VerticalAlignment = VerticalAlignment.Top,
            Child = body,
        };
    }

    public static WrapPanel Wrap(params Control[] controls)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, Space, Space);
            panel.Children.Add(control);
        }
        return panel;
    }

    public static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = MetaBrush,
        FontSize = FontBody,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Border Pill(TextBlock content)
    {
        content.Margin = new Thickness(9, 4);
        content.FontSize = FontMeta;
        return new Border
        {
            Background = CardBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius),
            Child = content,
        };
    }

    public static readonly Color Machine = Accent;
    public static readonly Color StateInfo = Color.Parse("#79C0FF");
    public static readonly Color Transitions = Color.Parse("#A371F7");

    public static Color ForClass(string cls) => cls switch
    {
        "hkbStateMachine" => Machine,
        "hkbStateMachineStateInfo" => StateInfo,
        "hkbStateMachineTransitionInfoArray" => Transitions,
        _ => ByFamily(cls),
    };

    private static Color ByFamily(string cls)
    {
        if (cls.Contains("StateMachine")) return Machine;
        if (cls.Contains("ClipGenerator")) return Color.Parse("#3FB950");
        if (cls.Contains("Sequence")) return Color.Parse("#2EA043");
        if (cls.Contains("Blender") || cls.Contains("Layer") || cls.Contains("Selector"))
            return Color.Parse("#D29922");
        if (cls.Contains("Transition")) return Transitions;
        if (cls.Contains("Modifier")) return Color.Parse("#DB6D28");
        return TextMeta;
    }
}
