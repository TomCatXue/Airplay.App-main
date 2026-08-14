using AirPlay.Core2.Models.Configs;
using AirPlay.Core2.Services;
using Makaretu.Dns;
using Microsoft.Extensions.DependencyInjection;

namespace AirPlay.Core2.Extensions;

public static class DependencyInjectionExtensions
{
    extension(IServiceCollection serviceDescriptors)
    {
        public void UseAirPlayService()
        {
            serviceDescriptors.AddOptions<AirTunesConfig>();
            serviceDescriptors.AddOptions<AirPlayConfig>();

            serviceDescriptors.AddSingleton<AirTunesService>();
            serviceDescriptors.AddHostedService(s => s.GetRequiredService<AirTunesService>());

            serviceDescriptors.AddSingleton<AirPlayService>();
            serviceDescriptors.AddHostedService(s => s.GetRequiredService<AirPlayService>());

            serviceDescriptors.AddSingleton<DacpDiscoveryService>();
            serviceDescriptors.AddHostedService(s => s.GetRequiredService<DacpDiscoveryService>());

            serviceDescriptors.AddSingleton<SessionManager>();
            serviceDescriptors.AddSingleton<MulticastService>(_ => new MulticastService(nics =>
                nics.Where(nic =>
                    nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                    nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                    nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Tunnel &&
                    !nic.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) &&
                    !nic.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase) &&
                    !nic.Name.Contains("Default Switch", StringComparison.OrdinalIgnoreCase) &&
                    !nic.Name.Contains("Mihomo", StringComparison.OrdinalIgnoreCase) &&
                    !nic.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) &&
                    !nic.Name.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase) &&
                    nic.GetIPProperties().UnicastAddresses.Any(a =>
                        a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(a.Address)))));
            serviceDescriptors.AddSingleton<AirPlayPublisher>();

            serviceDescriptors.AddHostedService(s => s.GetRequiredService<AirPlayPublisher>());
        }
    }
}

