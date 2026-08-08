using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

// The palette and the controls built from it, so the tool looks like one thing rather than a
// toolkit's defaults.
public static class Ux
{
    public static readonly Color Base = Color.Parse("#151515");
    public static readonly Color Card = Color.Parse("#222222");
    public static readonly Color CardHover = Color.Parse("#2C2C2C");
    public static readonly Color Border = Color.Parse("#3A3A3A");
    public static readonly Color Accent = Color.Parse("#0070E0");
    public static readonly Color TextTitle = Color.Parse("#E6E6E6");
    public static readonly Color TextMeta = Color.Parse("#A0A0A0");
    public static readonly Color TextMuted = Color.Parse("#787878");
    public static readonly Color TextDisabled = Color.Parse("#5A5A5A");
    public static readonly Color TextCode = Color.Parse("#00A0DA");
    public static readonly Color Bad = Color.Parse("#FF5555");
    public static readonly Color Warn = Color.Parse("#E0A030");
    public static readonly Color Good = Color.Parse("#3FB950");

    /// A transition on the canvas. Not a class colour: a route is an event the game sends rather
    /// than one object holding another, so it has to read as a different kind of thing from the
    /// ownership wires it is drawn over.
    public static readonly Color RouteColour = Color.Parse("#58D0C0");

    /// An event that enters a state from any state of its machine, written on the state.
    ///
    /// Its own colour rather than the warning amber it started as. A modifier's wires are orange and
    /// there are a great many of them crossing a behaviour graph, so amber text on a node read as
    /// part of that traffic. Nothing else on the canvas is pink.
    public static readonly Color Wildcard = Color.Parse("#F778BA");

    /// The outline drawn under a line that has been picked out, so lines running together stay
    /// countable instead of merging into one band.
    ///
    /// The canvas colour, not the opposite of it. A light casing was tried first and is wrong here
    /// for the case that needs it most: twenty five wildcard routes converge on one node, and giving
    /// each a bright edge welds them into a solid block. A casing in the background colour puts a gap
    /// between neighbours instead, which is what separates them.
    public static readonly Color Casing = Base;

    public static readonly IBrush BaseBrush = new SolidColorBrush(Base);
    public static readonly IBrush CardBrush = new SolidColorBrush(Card);
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
        Background = CardBrush,
        Foreground = TitleBrush,
        BorderBrush = BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(7, 4),
        FontSize = 12,
    };

    public static Button Primary(string text) => Style(new Button { Content = text }, AccentBrush, Brushes.White);

    public static Button Secondary(string text) => Style(new Button { Content = text }, CardBrush, MetaBrush);

    private static Button Style(Button button, IBrush background, IBrush foreground)
    {
        button.Background = background;
        button.Foreground = foreground;
        button.BorderBrush = BorderBrush;
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(3);
        button.Padding = new Thickness(11, 4);
        button.FontSize = 12;
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = MutedBrush,
        FontSize = 10,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(1, 2, 0, 2),
    };

    public static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = MetaBrush,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Border Pill(TextBlock content)
    {
        content.Margin = new Thickness(9, 4);
        return new Border
        {
            Background = CardBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = content,
        };
    }

    /// The machine itself, a state of one, and a state's list of routes out of it. Three different
    /// things, and the whole of a behaviour's shape is how they sit together.
    public static readonly Color Machine = Accent;
    public static readonly Color StateInfo = Color.Parse("#79C0FF");
    public static readonly Color Transitions = Color.Parse("#A371F7");

    // Node colouring by class family.
    //
    // The state machine family is matched by exact name, before anything is matched by substring.
    // `hkbStateMachineStateInfo` and `hkbStateMachineTransitionInfoArray` both contain
    // "StateMachine", so the first rule caught all three and the Transition rule below never fired
    // for any of them: a machine, its states and their routes all drew in the same colour, which is
    // the one distinction a state machine most needs on screen.
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
