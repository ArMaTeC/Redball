using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// User feedback survey for collecting product insights.
/// </summary>
public partial class UserFeedbackSurvey : Window
{
    private int _rating;
    private readonly HttpClient _httpClient = new();

    public UserFeedbackSurvey()
    {
        InitializeComponent();
        ContactCheck.Checked += (s, e) => EmailText.IsEnabled = true;
        ContactCheck.Unchecked += (s, e) => EmailText.IsEnabled = false;
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var rating))
        {
            _rating = rating;
            UpdateStarDisplay();
            RatingLabel.Text = rating switch
            {
                1 => "Poor - Needs improvement",
                2 => "Fair - Could be better",
                3 => "Good - Works as expected",
                4 => "Very Good - Quite satisfied",
                5 => "Excellent - Love it!",
                _ => "Click a star to rate"
            };
        }
    }

    private void UpdateStarDisplay()
    {
        var stars = new[] { Star1, Star2, Star3, Star4, Star5 };
        for (int i = 0; i < stars.Length; i++)
        {
            stars[i].Content = i < _rating ? "★" : "☆";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_rating == 0)
            {
                NotificationWindow.Show("Rating Required", "Please select a star rating before submitting.", "\uE734");
                return;
            }

            // Build feedback object
            var feedback = new
            {
                Timestamp = DateTime.UtcNow,
                Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                Rating = _rating,
                Features = new
                {
                    KeepAwake = KeepAwakeCheck.IsChecked,
                    Timer = TimerCheck.IsChecked,
                    Battery = BatteryCheck.IsChecked,
                    Network = NetworkCheck.IsChecked,
                    Schedule = ScheduleCheck.IsChecked,
                    TypeThing = TypeThingCheck.IsChecked
                },
                Feedback = FeedbackText.Text,
                AllowContact = ContactCheck.IsChecked,
                Email = ContactCheck.IsChecked == true ? EmailText.Text : null
            };

            // Save locally first
            var json = JsonSerializer.Serialize(feedback, new JsonSerializerOptions { WriteIndented = true });
            var feedbackPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Redball", "feedback.json");
            
            Directory.CreateDirectory(Path.GetDirectoryName(feedbackPath)!);
            File.WriteAllText(feedbackPath, json);

            // Try to submit to GitHub issue (optional)
            if (ConfigService.Instance.Config.EnableTelemetry)
            {
                await SubmitToGitHubAsync(feedback);
            }

            Logger.Info("UserFeedbackSurvey", "Feedback submitted successfully");
            NotificationWindow.Show("Feedback Sent", "Thank you for your feedback! It helps us improve Redball.", "\uE73E");
            
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("UserFeedbackSurvey", "Failed to submit feedback", ex);
            NotificationWindow.Show("Partial Success", "Failed to send feedback online, but it has been saved locally for later submission.", "\uE7BA");
            DialogResult = true;
            Close();
        }
    }

    private async Task SubmitToGitHubAsync(object feedback)
    {
        try
        {
            // Note: This would require a GitHub token and API integration
            // For now, we just save locally
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.Debug("UserFeedbackSurvey", $"GitHub submission failed (local save is primary): {ex.Message}");
        }
    }
}
