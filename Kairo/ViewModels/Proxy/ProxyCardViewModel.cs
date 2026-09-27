using System;
using System.ComponentModel;
using Kairo.Components;
using Kairo.Components.DashBoard;
using Kairo.Core.Models;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class ProxyCardViewModel : ViewModelBase
    {
        private readonly ProxyListPageViewModel _owner;
        private bool _isRunning;
        private bool _isSelected;
        private bool _isBusy;

        public Proxy Proxy { get; }

        public string Name => Proxy.ProxyName;
        public int Id => Proxy.Id;
        public string TypeText => string.IsNullOrWhiteSpace(Proxy.ProxyType) ? "-" : Proxy.ProxyType.ToUpperInvariant();
        public string LocalAddress => $"{Proxy.LocalIp}:{Proxy.LocalPort}";

        /// <summary>公网访问地址（http/https 为域名，tcp/udp 为节点地址:端口），点对点隧道为 null</summary>
        public string? PublicAddress => TunnelAddress.GetPublicAddress(Proxy.ProxyType, Proxy.NodeInfo?.Host, Proxy.NodeInfo?.Ip, Proxy.RemotePort, Proxy.Domain);

        public bool HasPublicAddress => !string.IsNullOrEmpty(PublicAddress);

        public string RouteText => $"{LocalAddress} → {PublicAddress ?? (TunnelAddress.IsPeerToPeer(Proxy.ProxyType) ? "点对点连接" : "-")}";

        public string NodeText
        {
            get
            {
                var node = Proxy.NodeInfo?.Name;
                if (string.IsNullOrWhiteSpace(node)) node = Proxy.NodeInfo?.Host ?? Proxy.NodeInfo?.Ip;
                if (string.IsNullOrWhiteSpace(node)) node = Proxy.Node > 0 ? $"节点 {Proxy.Node}" : "未知节点";
                return $"{node} · #{Proxy.Id}";
            }
        }

        public string StatusText => IsBusy ? "启动中…" : IsRunning ? "运行中" : "未启动";
        public string ToggleToolTip => IsRunning ? "停止隧道" : "启动隧道";

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (!SetProperty(ref _isRunning, value)) return;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ToggleToolTip));
                RaiseCommandStates();
            }
        }

        /// <summary>正在准备启动（例如 LoliaFRP 需要先获取隧道配置）</summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                OnPropertyChanged(nameof(StatusText));
                RaiseCommandStates();
            }
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
        public RelayCommand ToggleCommand { get; }
        public RelayCommand CopyAddressCommand { get; }

        public ProxyCardViewModel(Proxy proxy, ProxyListPageViewModel owner)
        {
            Proxy = proxy;
            _owner = owner;
            SelectCommand = new RelayCommand(() => _owner.Select(this));
            RefreshCommand = new AsyncRelayCommand(_owner.RefreshAsync);
            CreateCommand = new RelayCommand(() => _owner.RequestCreateWindow());
            DeleteCommand = new RelayCommand(async () => await _owner.DeleteProxyAsync(this));
            StartCommand = new RelayCommand(() => _owner.StartProxy(this), () => !IsRunning && !IsBusy);
            StopCommand = new RelayCommand(() => _owner.StopProxy(this), () => IsRunning);
            ToggleCommand = new RelayCommand(Toggle, () => !IsBusy);
            CopyAddressCommand = new RelayCommand(() => _owner.CopyAddress(this), () => HasPublicAddress);
        }

        private void Toggle()
        {
            if (IsRunning) _owner.StopProxy(this);
            else _owner.StartProxy(this);
        }

        private void RaiseCommandStates()
        {
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ToggleCommand.RaiseCanExecuteChanged();
        }
    }
}
