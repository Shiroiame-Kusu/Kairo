using System;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Utils;

namespace Kairo.ViewModels
{
    public class DashBoardViewModel : ViewModelBase
    {
        private bool _frpcChecked;
        private string _selectedTag = "home";
        private string _snackbarTitle = string.Empty;
        private string _snackbarMessage = string.Empty;
        private InfoBarSeverity _snackbarSeverity = InfoBarSeverity.Informational;
        private bool _isSnackbarOpen;

        public string SelectedTag
        {
            get => _selectedTag;
            set => SetProperty(ref _selectedTag, value);
        }

        public string SnackbarTitle
        {
            get => _snackbarTitle;
            private set => SetProperty(ref _snackbarTitle, value);
        }

        public string SnackbarMessage
        {
            get => _snackbarMessage;
            private set => SetProperty(ref _snackbarMessage, value);
        }

        public InfoBarSeverity SnackbarSeverity
        {
            get => _snackbarSeverity;
            private set => SetProperty(ref _snackbarSeverity, value);
        }

        public bool IsSnackbarOpen
        {
            get => _isSnackbarOpen;
            private set => SetProperty(ref _isSnackbarOpen, value);
        }

        public bool FrpcChecked
        {
            get => _frpcChecked;
            set => SetProperty(ref _frpcChecked, value);
        }

        public void ShowSnackbar(string title, string? message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            SnackbarTitle = title;
            SnackbarMessage = message ?? string.Empty;
            SnackbarSeverity = severity;
            IsSnackbarOpen = true;
        }

        public bool ShouldPromptFrpcDownload()
        {
            if (FrpcChecked) return false;
            FrpcChecked = true;
            var path = Global.Config.FrpcPath;
            return string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path);
        }

        public void CloseSnackbarLater(TimeSpan delay)
        {
            DispatcherTimer.RunOnce(() => IsSnackbarOpen = false, delay);
        }

        public void CloseSnackbar()
        {
            IsSnackbarOpen = false;
        }
    }
}
