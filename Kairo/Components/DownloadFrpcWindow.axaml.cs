using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kairo.ViewModels;

namespace Kairo.Components
{
    public partial class DownloadFrpcWindow : Window
    {
        private readonly DownloadFrpcWindowViewModel _viewModel;

        public DownloadFrpcWindow()
        {
            InitializeComponent();
            _viewModel = new DownloadFrpcWindowViewModel();
            DataContext = _viewModel;

            _viewModel.CloseRequested += CloseSafe;
            Opened += async (_, _) => await _viewModel.StartAsync();
            Closed += (_, _) => _viewModel.Dispose();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void CloseSafe()
        {
            try { Close(); }
            catch { }
        }
    }
}
