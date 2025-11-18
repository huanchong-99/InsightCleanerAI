using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using InsightCleanerAI.Resources;
using InsightCleanerAI.ViewModels;
using WinForms = System.Windows.Forms;

namespace InsightCleanerAI
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null || sender is not PasswordBox passwordBox)
            {
                return;
            }

            ViewModel.CloudApiKey = passwordBox.Password;
        }

        private void SearchApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null || sender is not PasswordBox passwordBox)
            {
                return;
            }

            ViewModel.SearchApiKey = passwordBox.Password;
        }

        private void LocalLlmApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null || sender is not PasswordBox passwordBox)
            {
                return;
            }

            ViewModel.LocalLlmApiKey = passwordBox.Password;
        }

        private void MaxNodesButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ApplyMaxNodesPreset();
        }

        private void BrowseCacheButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
            {
                return;
            }

            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = Strings.BrowseCacheDialogDescription,
                SelectedPath = Directory.Exists(ViewModel.CacheDirectory)
                    ? ViewModel.CacheDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                ViewModel.CacheDirectory = dialog.SelectedPath;
            }
        }

        private void BrowseDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = Strings.BrowseDatabaseDialogTitle,
                FileName = Path.GetFileName(ViewModel.DatabasePath),
                InitialDirectory = GetDatabaseDirectory(),
                Filter = Strings.DatabaseFileFilter,
                AddExtension = true,
                DefaultExt = ".db"
            };

            if (dialog.ShowDialog() == true)
            {
                ViewModel.DatabasePath = dialog.FileName;
            }
        }

        private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
            {
                return;
            }

            var confirmation = MessageBox.Show(
                Strings.ClearCacheConfirm,
                Strings.ClearCacheTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            if (ViewModel.TryClearCache(out var message))
            {
                MessageBox.Show(message, Strings.ClearCacheDoneTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(message, Strings.ClearCacheErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
            {
                return;
            }

            Infrastructure.DebugLog.Info($"保存为默认设置 - AiMode={ViewModel.SelectedAiMode}, LocalLlmModel={ViewModel.LocalLlmModel}, CloudModel={ViewModel.CloudModel}");
            ViewModel.SaveConfiguration(includeSensitive: false);
            Infrastructure.DebugLog.Info("默认设置已保存");
            MessageBox.Show(
                Strings.SaveDefaultsMessage,
                Strings.ClearCacheDoneTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                Infrastructure.DebugLog.Info($"设置窗口关闭 - 保存配置前 AiMode={ViewModel.SelectedAiMode}, LocalLlmModel={ViewModel.LocalLlmModel}");
                ViewModel.SaveConfiguration();
                Infrastructure.DebugLog.Info("设置已保存");
            }
            Close();
        }

        private async void FetchCloudModelsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ViewModel.RemoteServerUrl))
            {
                MessageBox.Show(
                    "请先填写云端服务地址",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await ViewModel.LoadCloudModelsAsync();

            if (ViewModel.CloudModels.Count == 0)
            {
                MessageBox.Show(
                    "未能获取到模型列表，请检查：\n1. 服务地址是否正确\n2. API Key是否有效\n3. 网络连接是否正常",
                    "获取失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    $"成功获取 {ViewModel.CloudModels.Count} 个模型",
                    "获取成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async void FetchLocalModelsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ViewModel.LocalLlmEndpoint))
            {
                MessageBox.Show(
                    "请先填写本地LLM服务地址",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await ViewModel.LoadLocalModelsAsync();

            if (ViewModel.LocalModels.Count == 0)
            {
                MessageBox.Show(
                    "未能获取到模型列表，请检查：\n1. 本地LLM服务是否已启动\n2. 服务地址是否正确\n3. 是否支持模型列表接口",
                    "获取失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    $"成功获取 {ViewModel.LocalModels.Count} 个模型",
                    "获取成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private string GetDatabaseDirectory()
        {
            if (ViewModel is null)
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            var directory = Path.GetDirectoryName(ViewModel.DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
    }
}

