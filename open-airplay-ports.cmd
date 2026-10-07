@echo off
chcp 65001 >nul
echo Opening AirPlay inbound ports...

rem 1) mDNS / Bonjour device discovery (UDP 5353) - REQUIRED, otherwise iPhone cannot find this PC
netsh advfirewall firewall delete rule name="AirPlay mDNS 5353" >nul 2>&1
netsh advfirewall firewall add rule name="AirPlay mDNS 5353" dir=in action=allow protocol=UDP localport=5353 profile=any

rem 2) AirPlay RTSP control (TCP 7100)
netsh advfirewall firewall delete rule name="AirPlay RTSP 7100" >nul 2>&1
netsh advfirewall firewall add rule name="AirPlay RTSP 7100" dir=in action=allow protocol=TCP localport=7100 profile=any

rem 3) AirPlay / AirTunes timing ports (UDP 7000-7020)
netsh advfirewall firewall delete rule name="AirPlay Timing UDP 7000-7020" >nul 2>&1
netsh advfirewall firewall add rule name="AirPlay Timing UDP 7000-7020" dir=in action=allow protocol=UDP localport=7000-7020 profile=any

rem 4) AirTunes audio (TCP/UDP 5000-5020)
netsh advfirewall firewall delete rule name="AirPlay AirTunes 5000-5020" >nul 2>&1
netsh advfirewall firewall add rule name="AirPlay AirTunes 5000-5020" dir=in action=allow protocol=TCP localport=5000-5020 profile=any
netsh advfirewall firewall add rule name="AirPlay AirTunes 5000-5020" dir=in action=allow protocol=UDP localport=5000-5020 profile=any

echo.
echo Done. Rules added:
netsh advfirewall firewall show rule name=all dir=in | findstr /i "AirPlay"
echo.
pause
