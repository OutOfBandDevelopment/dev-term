# Korad KA Series Remote Control Syntax V4.0

Transcription of "KA Series Remote Control Syntax V4.0" (3 pages), rendered
from `KA Series Single Channel Remote Control Syntax V4.0.pdf` (not in the
repo — see the user's own manual copy, extracted from `KA KD SERIES_V2.4.zip`).
Primary source material — transcribed as printed, not summarized. Per the
manual's own title and its `*IDN?` example, this document is the remote
command syntax for the Korad KA3005P; the user has confirmed it also applies
to the Korad KA6003P (both single-channel KA-series bench power supplies
sharing this command set).

<!-- page 1 -->

## KA Series Remote Control Syntax V4.0

**Command format:** `VSET<X>:<NR2>`

1. `VSET`: Command header
2. `X`: output channel
3. separator
4. `NR2`: parameter

**Command Details:**

**1. `ISET<X>:<NR2>`**

Description: Sets the output current.
Example: **ISET1:2.225**
Sets the CH1 output current to 2.225A

**2. `ISET<X>?`**

Description: Returns the output current setting.
Example: **ISET1?**
Returns the CH1 output current setting

**3. `VSET<X>:<NR2>`**

Description: Sets the output voltage.
Example: **VSET1:20.50**
Sets the CH1 voltage to 20.50V

**4. `VSET<X>?`**

Description: Returns the output voltage setting.
Example: **VSET1?**
Returns the CH1 voltage setting.

**5. `IOUT<X>?`**

Description: Returns the actual output current.
Example: **IOUT1?**
Returns the CH1 output current

**6. `VOUT<X>?`**

Description: Returns the actual output voltage.
Example: **VOUT1?**
Returns the CH1 output voltage

<!-- page 2 -->

**7. `BEEP<Boolean>`**

Description: Turns on or off the beep. Boolean: boolean logic.
Example: **BEEP1** Turns on the beep.

**8. `OUT<Boolean>`**

Description: Turns on off the output.
Boolean: 0 OFF, 1 ON
Example: **OUT1** Turns on the output

**9. `STATUS?`**

Description: reading the status of the power supply returning
Contents: 8 bits follow the below formats

| Bit | Content | Description |
|---|---|---|
| 0 | CH1 | 0=CC mode, 1=CV mode |
| 1 | CH2 | 0=CC mode, 1=CV mode |
| 2, 3 | Tracking | 01=independent, 11=serial connection, 10=parallel connection |
| 4 | Beep | 0=OFF, 1=ON |
| 5 | OCP | 0=OCP OFF, 1=OCP ON |
| 6 | Output | 0=OFF, 1=ON |
| 7 | OVP | 0=OVP OFF, 1=OVP ON |

**10. `*IDN?`**

Description: Returns the KA3005P identification.
Example **\*IDN?**
Contents `KORAD KA3005P V4.0` (Manufacturer, model name,).

**11. `RCL<NR1>`**

Description: Recalls a panel setting.
NR1 1-5: Memory number 1 to 5
Example **RCL1** Recalls the panel setting stored in memory number1

**12. `SAV<NR1>`**

Description: Stores the panel setting.
NR1 1-5: Memory number 1 to 5
Example **SAV1** Stores the panel setting stored in memory number1

**13. `OCP<Boolean>`**

Description: Stores the panel setting.
Boolean: 0 OFF, 1 ON
Example: **OCP1** Turns on the OCP

<!-- page 3 -->

**14. `OVP<Boolean>`**

Description: Turns on the OVP.
Boolean: 0 OFF, 1 ON
Example: **OVP1** Turns on the OVP
