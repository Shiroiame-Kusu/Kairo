using System;
using System.ComponentModel;
using Kairo.Components;
using Kairo.Components.DashBoard;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class ProxyCardViewModel : ViewModelBase
    {
        private readonly ProxyListPageViewModel _owner;
        private bool _isRunning;
        private bool _isSelected;

        public Proxy Proxy { get; }

        public string Name => Proxy.ProxyName;
        public string RouteText
        {
            get
            {
                var nodeLabel = Proxy.NodeInfo?.Host ?? Proxy.NodeInfo?.Ip ?? (Proxy.Node > 0 ? $"Node{Proxy.Node}" : "Node");
                var remote = Proxy.RemotePort.HasValue ? Proxy.RemotePort.Value.ToString() : (Proxy.Domain ?? "-");
                return $"{Proxy.LocalIp}:{Proxy.LocalPort} -> {nodeLabel}:{remote}";
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public RelayCommand SelectCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }
        public RelayCommand CreateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand { get; }

        public ProxyCardViewModel(Proxy proxy, ProxyListPageViewModel owner)
        {
            Proxy = proxy;
            _owner = owner;
            SelectCommand = new RelayCommand(() => _owner.Select(this));
            RefreshCommand = new AsyncRelayCommand(_owner.RefreshAsync);
            CreateCommand = new RelayCommand(() => _owner.RequestCreateWindow());
            DeleteCommand = new RelayCommand(async () => await _owner.DeleteProxyAsync(this));
            StartCommand = new RelayCommand(() => _owner.StartProxy(this));
            StopCommand = new RelayCommand(() => _owner.StopProxy(this));
        }
    }
}
