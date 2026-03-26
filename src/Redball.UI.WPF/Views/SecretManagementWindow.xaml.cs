// Copyright (c) ArMaTeC. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class SecretManagementWindow : Window
{
    public SecretManagementWindow()
    {
        InitializeComponent();
        _ = RefreshSecretsList();
    }

    private async Task RefreshSecretsList()
    {
        try
        {
            var secrets = await SecretManagerService.Instance.ListSecretsAsync();
            var items = secrets.Select(key => new SecretViewModel
            {
                Key = key,
                Status = "Stored",
                Provider = "Windows Credential Manager"
            }).ToList();
            
            // Add known secrets that aren't stored yet
            var knownSecrets = new[]
            {
                SecretManagerService.KnownSecrets.CloudAnalyticsApiKey,
                SecretManagerService.KnownSecrets.CloudAnalyticsEndpoint,
                SecretManagerService.KnownSecrets.UpdatePublisherThumbprint,
                SecretManagerService.KnownSecrets.WebApiAuthToken
            };
            
            foreach (var known in knownSecrets)
            {
                if (!items.Any(i => i.Key == known))
                {
                    items.Add(new SecretViewModel
                    {
                        Key = known,
                        Status = "Not Configured",
                        Provider = "-"
                    });
                }
            }
            
            SecretsDataGrid.ItemsSource = items.OrderBy(i => i.Key).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load secrets: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddSecret_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SecretEditorDialog();
        if (dialog.ShowDialog() == true && dialog.SecretKey != null)
        {
            _ = SaveSecretAsync(dialog.SecretKey, dialog.SecretValue);
        }
    }

    private void EditSecret_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string key)
        {
            var dialog = new SecretEditorDialog { SecretKey = key, IsEditMode = true };
            if (dialog.ShowDialog() == true)
            {
                _ = SaveSecretAsync(key, dialog.SecretValue);
            }
        }
    }

    private async Task SaveSecretAsync(string key, string value)
    {
        try
        {
            var success = await SecretManagerService.Instance.StoreSecretAsync(key, value);
            if (success)
            {
                MessageBox.Show("Secret saved successfully.", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshSecretsList();
            }
            else
            {
                MessageBox.Show("Failed to save secret.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving secret: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteSecret_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string key)
        {
            var result = MessageBox.Show($"Delete secret '{key}'?", "Confirm", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await SecretManagerService.Instance.DeleteSecretAsync(key);
                    await RefreshSecretsList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting secret: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSecretsList();
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var status = SecretManagerService.Instance.GetHealth();
            MessageBox.Show(
                $"Primary Provider: {status.PrimaryProvider}\n" +
                $"Primary Available: {status.PrimaryAvailable}\n" +
                $"Fallback Available: {status.FallbackAvailable}\n" +
                $"Timestamp: {status.Timestamp}",
                "Provider Health", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Health check failed: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private class SecretViewModel
    {
        public string Key { get; set; } = "";
        public string Status { get; set; } = "";
        public string Provider { get; set; } = "";
    }
}
