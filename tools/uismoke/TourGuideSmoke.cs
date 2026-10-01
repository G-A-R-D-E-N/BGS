using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BehaviourStudio.App;
using AppHost = BehaviourStudio.App.App;

namespace BehaviourStudio.UiSmoke;

internal static class TourGuideSmoke
{
    internal static void Run()
    {
        var window = AppHost.CreateMainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            foreach (string completion in new[] { "Skip tour", "Done" })
            {
                Click("Take the tour");
                var overlay = Smoke.Find<TourOverlay>(window).Single();
                string[] titles = { "Set up AI chat", "Ask the assistant", "Review each proposed action", "Check the result and save" };
                int seen = 0;
                for (int step = 0; step < 80 && overlay.IsActive; step++)
                {
                    if (seen < titles.Length && Smoke.Find<TextBlock>(overlay).Any(text => text.Text == titles[seen])) seen++;
                    if (Smoke.Find<Button>(overlay).Any(button => button.Content?.ToString() == "Done")) break;
                    Click("Next");
                }
                if (seen != titles.Length) throw new InvalidOperationException("AI chat guide is missing or out of order in the tour");
                if (!Smoke.Find<TextBlock>(overlay).Any(text => (text.Text ?? "").Contains("not that an edit succeeded")))
                    throw new InvalidOperationException("AI guide must distinguish dispatch from save");
                Click(completion);
                if (overlay.IsActive) throw new InvalidOperationException("Tour did not finish");
            }
            Console.WriteLine("AI tour guide and replay smoke PASS");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }

        void Click(string label)
        {
            Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == label)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
    }
}
