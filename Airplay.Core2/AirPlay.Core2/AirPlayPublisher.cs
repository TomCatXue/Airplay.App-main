using AirPlay.Core2.Models.Configs;
using AirPlay.Core2.Models.Messages.Rtsp;
using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace AirPlay.Core2;

public partial class AirPlayPublisher(MulticastService multicastService, ILogger<AirPlayPublisher> logger,
    IOptions<AirTunesConfig> airTunesConfig, IOptions<AirPlayConfig> airPlayConfig) : IHostedService
{
    private readonly ServiceDiscovery _serviceDiscovery = new(multicastService);

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        var matches = MacRegex.Match(airTunesConfig.Value.MacAddress);
        if (!matches.Success) throw new ArgumentException("Must be a mac address");

        var deviceIdInstance = string.Join(string.Empty, matches.Groups[2].Captures) + matches.Groups[3].Value;
        var lanAddress = GetLanIPv4Address();

        #region AirTunes Service

        ServiceProfile airTunesProfile = new
        (
            $"{deviceIdInstance}@{airTunesConfig.Value.ServiceName}",
            AirTunesType,
            airTunesConfig.Value.Port
        );

        //airTunesProfile.AddProperty("ch", "2");
        airTunesProfile.AddProperty("cn", "0,1,2"); // compressionTypes: 0=pcm, 1=alac, 2=aac, 3=aac-eld (not supported here)
        airTunesProfile.AddProperty("da", "true"); // rfc2617DigestAuthKey
        airTunesProfile.AddProperty("et", "0,3,5"); // encryptionTypes: 0=none, 1=rsa (airport express), 3=fairplay, 4=MFiSAP, 5=fairplay SAPv2.5
        airTunesProfile.AddProperty("ft", Constants.FEATURES); // originally "0x5A7FFFF7,0x1E" https://openairplay.github.io/airplay-spec/features.html
        airTunesProfile.AddProperty("sf", "0x4"); //systemFlags
        airTunesProfile.AddProperty("md", "0,1,2"); // metadataTypes 0=text, 1=artwork, 2=progress
        airTunesProfile.AddProperty("am", Constants.DEVICE_MODEL); // deviceModel
        airTunesProfile.AddProperty("pw", "false"); // password
        airTunesProfile.AddProperty("pk", "03a107bff3ce10be1d70dd18e74bc09967e4d6309ba50d5f1ddc8664125531b8"); // publicKey
        airTunesProfile.AddProperty("tp", "UDP"); // transportTypes
        airTunesProfile.AddProperty("vn", "65537");
        airTunesProfile.AddProperty("vs", Constants.AIPLAY_SERVICE_VERSION);
        airTunesProfile.AddProperty("ov", "11"); // 	vodkaVersion
        airTunesProfile.AddProperty("vv", "2"); // 	vodkaVersion

        //airTunesProfile.AddProperty("sr", "44100"); // sample rate
        //airTunesProfile.AddProperty("ss", "16"); // bitdepth
        //airTunesProfile.AddProperty("sv", "false"); // unk

        if (lanAddress != null)
            airTunesProfile.Resources.Add(new ARecord { Name = airTunesProfile.HostName, Address = lanAddress });

        _serviceDiscovery.Advertise(airTunesProfile);
        logger.AirTunesPublished(airTunesConfig.Value.Port);

        #endregion

        #region AirPlay Service

        ServiceProfile airPlayProfile = new
        (
            $"{deviceIdInstance}@{airPlayConfig.Value.ServiceName}",
            AirPlayType,
            airPlayConfig.Value.Port
        );

        airPlayProfile.AddProperty("acl", "0"); // accessControlLevel
        airPlayProfile.AddProperty("deviceid", airTunesConfig.Value.MacAddress);
        airPlayProfile.AddProperty("features", Constants.FEATURES); // originally "0x5A7FFFF7,0x1E" https://openairplay.github.io/airplay-spec/features.html
        airPlayProfile.AddProperty("rsf", "0x0"); // requiredSenderFeatures
        airPlayProfile.AddProperty("flags", "0x4");
        airPlayProfile.AddProperty("model", Constants.DEVICE_MODEL);
        airPlayProfile.AddProperty("protovers", "1.1");
        airPlayProfile.AddProperty("srcvers", Constants.AIPLAY_SERVICE_VERSION);
        airPlayProfile.AddProperty("pi", "1842bdae-8a92-b965-f657-5efd9b909b1a");
        airPlayProfile.AddProperty("gid", "d2e4a324-bfa0-7535-d42a-9048f1ad20ca");
        airPlayProfile.AddProperty("gcgl", "0");
        //airPlayProfile.AddProperty("vv", "2");
        airPlayProfile.AddProperty("pk", "03a107bff3ce10be1d70dd18e74bc09967e4d6309ba50d5f1ddc8664125531b8"); // publicKey

        if (lanAddress != null)
            airPlayProfile.Resources.Add(new ARecord { Name = airPlayProfile.HostName, Address = lanAddress });

        _serviceDiscovery.Advertise(airPlayProfile);
        logger.AirPlayPublished(airPlayConfig.Value.Port);

        #endregion

        multicastService.Start();

        return Task.CompletedTask;
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        _serviceDiscovery.Dispose();
        multicastService.Stop();

        return Task.CompletedTask;
    }
}

partial class AirPlayPublisher
{
    public const string AirPlayType = "_airplay._tcp";
    public const string AirTunesType = "_raop._tcp";

    public static Regex MacRegex = GenMacRegex();

    private static IPAddress? GetLanIPv4Address()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            if (nic.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.Contains("Default Switch", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.Contains("Mihomo", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase)) continue;

            var ipv4 = nic.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address)
                .FirstOrDefault(a => !IPAddress.IsLoopback(a));
            if (ipv4 != null) return ipv4;
        }
        return null;
    }

    [GeneratedRegex("^(([0-9a-fA-F][0-9a-fA-F]):){5}([0-9a-fA-F][0-9a-fA-F])$")]
    private static partial Regex GenMacRegex();
}

internal static partial class AirPlayPublisherLoggers
{
    [LoggerMessage(LogLevel.Information, "AirTunes Service [{port}] Published on mDns")]
    public static partial void AirTunesPublished(this ILogger logger, ushort port);

    [LoggerMessage(LogLevel.Information, "AirPlay Service [{port}] Published on mDns")]
    public static partial void AirPlayPublished(this ILogger logger, ushort port);
}

