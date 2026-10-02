# RFC2217-like Function Example

## 1. Downloads

- USR-TCP232-302/304/306 user manual: <https://www.usr.cn/Download/920.html>
- USR-TCP232-302/304/306 specification: <https://www.usr.cn/Download/405.html>
- USR-TCP232-302/304/306 AT command set: <https://www.usr.cn/Download/1255.html>
- M0 series setup software: <https://www.usr.cn/Download/257.html>
- Combined network / serial debugging assistant: <https://www.usr.cn/Download/27.html>
- USR virtual serial port software V3.7.2.529: <https://www.usr.cn/Download/31.html>
- USR virtual serial port software V5.0.1: <https://www.usr.cn/Download/924.html>

## 2. Hardware Connection

### 2.1 Required Items

- USR-TCP232-302 × 1
- 5 V power adapter × 1
- USB-RS232 serial cable × 1
- CAT5e Ethernet cable × 1
- Laptop × 1

### 2.2 Hardware Connection

Connect the RS232 port of the USR-TCP232-302 to a USB port on the computer using the USB-RS232 serial cable. Connect the Ethernet port directly to the computer with the Ethernet cable, then power the product with the 5 V power adapter.

Diagram:

```plantuml
@startuml
left to right direction
component "Serial debugging\nassistant 1" as S1
component "USR-TCP232-302" as DEV
component "Virtual serial port\nsoftware (USR-VCOM)" as VC
component "Serial debugging\nassistant 2" as S2
S1 -- DEV : RS232
DEV -- VC : Ethernet
VC -- S2 : Virtual COM
@enduml
```

## 3. Product Parameter Settings

### 3.1 Computer Environment Settings

Open the Control Panel, keep only the local area connection network adapter enabled and disable all other adapters. Right-click the local area connection, open Properties, find IPv4 and set a static IP: IP address 192.168.0.201, subnet mask 255.255.255.0, gateway 192.168.0.1. (The factory default IP of the USR-TCP232-304 is 192.168.0.7; the computer must be on the same subnet as the device when configuring it.)

```plantuml
@startsalt
{+
  Internet Protocol Version 4 (TCP/IPv4) Properties
  ..
  {/ <b>General | Alternate Configuration }
  Use the following IP settings:
  ( ) Obtain an IP address automatically
  (X) Use the following IP address:
  {
    IP address: | "192 . 168 . 0 . 201 "
    Subnet mask: | "255 . 255 . 255 . 0 "
    Default gateway: | "192 . 168 . 0 . 1 "
  }
  ( ) Obtain DNS server address automatically
  (X) Use the following DNS server addresses:
  {
    Preferred DNS server: | "192 . 168 . 0 . 1 "
    Alternate DNS server: | "223 . 5 . 5 . 5 "
  }
  [ ] Validate settings upon exit
  .
  { [OK] | [Cancel] }
}
@endsalt
```

Turn off the computer's firewall and antivirus software.

```plantuml
@startsalt
{+
  Windows Defender Firewall > Customize Settings
  ..
  <b>Private network settings
  ( ) Turn on Windows Defender Firewall
  (X) Turn off Windows Defender Firewall (not recommended)
  ..
  <b>Public network settings
  ( ) Turn on Windows Defender Firewall
  (X) Turn off Windows Defender Firewall (not recommended)
  .
  { [OK] | [Cancel] }
}
@endsalt
```

### 3.2 Virtual Serial Port Settings

Open virtual serial port software V3.

Create a virtual serial port COM2 with network protocol TCP Server and local port 8234, then click OK. The software begins listening on the network.

```plantuml
@startsalt
{+
  USR Virtual Serial Port Software V3.7.2.529
  { Device(D) | Tools(O) | Options(O) | English | Help(H) }
  ..
  {
    { [Add] | [Delete] | [Connect] | [Reset Count] | [Monitor] | [Search] | [Auto Create] | [Exit] }
  }
  ==
  {#
    Remarks | Serial Port | Port Status | Net Protocol | Target IP | Target Port | Local Port | Baud Rate
  }
  .
  {+
    Add Virtual Serial Port
    {
      Virtual serial port: | ^COM2^
      Network protocol: | ^TCP Server^
      Local IP: | "192.168.0.201 " (disabled)
      Target port: | "20108 " (disabled)
      Local port: | "8234 "
      Remarks: | "          "
    }
    { [OK] | [Cancel] | [Advanced +] }
  }
}
@endsalt
```

Enable the sync baud rate (RFC2217-like) option in the virtual serial port software's Options menu.

```plantuml
@startsalt
{+
  USR Virtual Serial Port Software V3.7.2.529
  { Device(D) | Tools(O) | <b>Options(O)</b> | English | Help(H) }
  ..
  {+
    Start on boot
    Keep-Alive
    Run at startup
    [X] Sync baud rate (RFC2217-like)
    Hide window
    (other items not legible in the original screenshot)
  }
}
@endsalt
```

### 3.3 USR-TCP232-302 Parameter Settings

Open the M0 software on the computer and click Search. After the device is found, double-click its MAC address; the USR-TCP232-302 parameters appear on the right.

Keep the USR-TCP232-302 IP at the factory default of 192.168.0.7. Set the module work mode to TCP Client, local port to 0, target IP/domain to 192.168.0.201, and remote port to 8234. Disable short connection and enable the RFC2217-like function. Save the parameters.

```plantuml
@startsalt
{+
  USR-M0 V2.2.5.1
  { File | Language | Help }
  ..
  {
    {/ <b>Operate via network | Operate via serial port }
    {#
      Device IP | Device name | MAC address | Version
      192.168.0.7 | USR-TCP232-302 | D4 AD 20 3D DB 11 | 4018
    }
    [ Search devices ]
    ..
    {+
      Operation log
      Data sent
      Data sent
      Click a discovered device to read its parameters; right-click the device list for more functions
      Read [ Mac : D4 AD 20 3D DB 11 ]
      Data sent
      Read complete
    }
    ==
    {+
      <b>Basic settings (items without ★ are normally left at default)
      {
        IP address type ★ | ^Static IP^ | HTTP server port | "80 "
        Module static IP ★ | "192.168.0.7 " | User name | "admin "
        Subnet mask ★ | "255.255.255.0 " | Password | "admin "
        Gateway ★ | "192.168.0.1 " | Device name | "USR-TCP23 "
        DNS address | "208.67.222.222 " | [ ] Index
        Timeout restart time (s) | "3600 " | [ ] Reset
        [ ] Clear cached data | [ ] Serial port settings | [X] Link
        . | . | [X] RFC2217
      }
    }
    {+
      <b>Port settings
      {
        Parity/Data/Stop | ^NONE^ ^8^ ^1^ | Serial baud rate | ^115200^
        Module work mode | ^TCP Client^ | Local port | "0 "
        Target IP/domain | "192.168.0.201 " | Remote port | "8234 "
        Short connection time | "3 " | TCP Server connection count | ^4^ (disabled)
        [ ] Enable short connection
        [X] TCP Server - kick old connection
      }
    }
    {+
      <b>Heartbeat packet
      Heartbeat enable | ^Heartbeat off^
    }
    [ Save parameters ]
  }
}
@endsalt
```

> **Note:** RFC2217-like is a simplified version of the RFC2217 protocol. Used together with the virtual serial port software, it lets the serial parameters of the 30X (baud rate, data bits, parity, etc.) be changed dynamically, so the 30X can communicate with devices whose serial parameters change. After this function is enabled, also enable the RFC2217-like function in the USR-VCOM virtual serial port software. The serial baud rate of the application software on the computer and the serial baud rate of the 30X then automatically match each other, with no need to pay attention to the serial baud rate setting!!!

## 4. Function Debugging

### 4.1 Function Debugging

Open two serial debugging assistants, one using the virtual serial port COM2 and the other using the COM port of the USB-to-RS232 converter. Change the baud rate in the serial debugging assistants to different values and send data to each other as a test; data is exchanged successfully.

### 4.2 Debugging Screenshots

The original shows four screenshots of the two serial debugging assistant windows (pairs of windows at different baud rates) exchanging data. The text in them is too small to read reliably; the layout common to every window is shown below.

```plantuml
@startsalt
{+
  Serial debugging assistant
  {
    {+
      <b>Serial port settings
      Port: | ^COM2 / USB-RS232 COM port^
      Baud rate: | ^(varied between tests)^
      Parity: | ^NONE^
      Data bits: | ^8^
      Stop bits: | ^1^
      (X) Close
    }
    {+
      <b>Receive settings
      (X) ASCII | ( ) HEX
      [X] Auto line break on receive
      [X] Show received data
      [ ] Save received data to file
      [ ] Pause receive display
    }
    {+
      <b>Send settings
      (X) ASCII | ( ) HEX
      [ ] Auto send additional bits
      [ ] Auto checksum
      [ ] Open file data source
      [ ] Cycle send  | "2000 " ms
    }
  }
  ..
  Data log (timestamped sent/received ASCII data)
  .
  "                      " [ Send ]
}
@endsalt
```

---

Author: Yin Congxin  Written: 2024-04-26
Reviewer: Yin Congxin  Reviewed: 2024-04-28
Revision: V1.0  Revision notes: First draft
