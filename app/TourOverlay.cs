using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BehaviourStudio.App;

public class TourOverlay : UserControl
{
    private readonly Avalonia.Controls.Shapes.Path _dim = new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
    };
    private readonly TextBlock _stepLabel = new() { Foreground = Ux.MutedBrush, FontSize = 11 };
    private readonly TextBlock _title = new()
    {
        Foreground = Ux.TitleBrush,
        FontSize = 16,
        FontWeight = FontWeight.Bold,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _desc = new()
    {
        Foreground = Ux.MetaBrush,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button _skip = Ux.Secondary("Skip tour");
    private readonly Button _next = Ux.Primary("Next");

    private readonly List<(Control Target, string Title, string Desc)> _steps = new();
    private int _index;
    private Action? _onFinished;

    public TourOverlay()
    {
        IsVisible = false;

        _skip.Click += (_, _) => Finish();
        _next.Click += (_, _) =>
        {
            if (_index >= _steps.Count - 1) Finish();
            else
            {
                _index++;
                ShowStep();
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_skip);
        buttons.Children.Add(_next);

        var bubble = new Border
        {
            Background = Ux.CardBrush,
            BorderBrush = Ux.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            MaxWidth = 480,
            Margin = new Thickness(24, 0, 24, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _stepLabel,
                    _title,
                    _desc,
                    buttons,
                },
            },
        };

        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(_dim);
        root.Children.Add(bubble);
        Content = root;
    }

    public bool IsActive => IsVisible;

    public void Start(IEnumerable<(Control Target, string Title, string Desc)> steps, Action? onFinished = null)
    {
        _steps.Clear();
        _steps.AddRange(steps);
        _onFinished = onFinished;
        _index = 0;
        IsVisible = true;
        ShowStep();
    }

    public void Skip() => Finish();

    private void ShowStep()
    {
        if (_steps.Count == 0)
        {
            Finish();
            return;
        }

        var (target, title, desc) = _steps[_index];
        _title.Text = title;
        _desc.Text = desc;
        _stepLabel.Text = $"Step {_index + 1} of {_steps.Count}";
        _next.Content = _index == _steps.Count - 1 ? "Done" : "Next";
        _dim.Data = null;

        target.BringIntoView();
        Dispatcher.UIThread.Post(Spotlight, DispatcherPriority.Loaded);
    }

    private void Spotlight()
    {
        var (target, _, _) = _steps[_index];
        var origin = target.TranslatePoint(new Point(0, 0), this);
        if (origin == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var hole = new Rect(origin.Value.X - 14, origin.Value.Y - 14,
                            target.Bounds.Width + 28, target.Bounds.Height + 28);
        if (hole.Right < 0 || hole.Bottom < 0 || hole.Left > Bounds.Width || hole.Top > Bounds.Height) return;

        double x = Math.Max(0, hole.X);
        double y = Math.Max(0, hole.Y);
        double w = Math.Min(Bounds.Width, hole.Right) - x;
        double h = Math.Min(Bounds.Height, hole.Bottom) - y;
        if (w <= 0 || h <= 0) return;

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, Bounds.Width, Bounds.Height) });
        group.Children.Add(new RectangleGeometry { Rect = new Rect(x, y, w, h) });
        _dim.Data = group;
    }

    private void Finish()
    {
        IsVisible = false;
        _dim.Data = null;
        _onFinished?.Invoke();
        _onFinished = null;
    }
}
