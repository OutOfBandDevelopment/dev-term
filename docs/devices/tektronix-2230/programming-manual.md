# Tektronix 2230 Programming Manual

OCR transcription of the Tektronix 2230 Programming Manual (42 pages),
rendered from `Tektronix_2230_Programming_Manual.pdf` (not in the
repo — see the user's own manual copy). Primary source material —
transcribed as printed, not summarized. Figures/diagrams/photos are
noted with placeholders, not redrawn.

**Signal names with a printed overbar** (indicating an active-low/
inverted signal, e.g. a line over `RESET`) are written with a leading
`/` instead (`/RESET`) — the standard plain-text convention for a
signal that can't be typeset with a real overline, and one that stays
grep-able (searching `RESET` still finds `/RESET`).

<!-- page (title page, unnumbered) -->

# Tektronix

## Model 2230 Digital Oscilloscope

## Programming Manual

<!-- page 7-10 -->

1. Perform a serial poll.

2. Send an EVENT? query to the oscilloscope requesting service.

3. If the EVENT? query response is not zero, then perform the response required to handle the event.

4. Return to the main program.

## OPTION 12 RS-232-C OPERATORS INFORMATION

The RS-232-C Communications interface conforms to the Tektronix standard on Codes, Formats, Conventions, and Features for messages sent over to bus for communications to other RS-232-C devices. Specific formats implemented in the 2200 DSO family for the Option 12 Communications interface are listed in Table 7-7. Specific feature implementation is shown in Table 7-8.

### Option 12 Side Panel

The side panel for Option 12 instruments (Figure 7-3) includes one AUXILIARY connector, one RS-232-C interface port (providing both DTE and DCE capability), and one PARAMETERS switch. The Controls, Connectors, and Indicators part of this manual contains information on the use of the AUXILIARY Connector. Refer to Figure 7-3 for location of the Option 12 side-panel controls and connectors.

**AUXILIARY Connector**—Provides connections for an X-Y Plotter and an External Clock input (see Controls, Connectors, and Indicators).

**RS-232-C PARAMETER Switch**—Allows the selection of setup options for the RS-232-C interface. The switches are read at power-up. Four sections of the switch select the baud rate, three select parity, one selects the terminator, and two are for printer/plotter selection. The function of each switch section is shown in Table 7-11.

**NOTE**

*Do not hook up external devices to the DTE connector and the DCE connector at the same time.*

**RS-232-C DTE Connector**—Provides connection meeting the EIA RS-232-C standard for data terminal equipment (see Figure 7-3). Table 7-9 lists the function of each pin of the connector. This connector is provided only on Option 12 instruments.

### Table 7-7: Specific Format Choices for Option 12

| Format Parameter | Choice Made |
|---|---|
| Format Characters | Not transmitted; ignored on reception. |
| Message Terminator | Either CR or CR-LF may be selected as the message terminator. |
| Measurement Terminator | Follows program message-unit syntax. |
| Link Data (Arguments) | Used in sending and receiving messages. |
| Multiple Event | Not implemented to report multiple events on a single reporting query. Multiple events may be reported by multiple queries. |
| Instrument Identification Query | Descriptors added for all options. |
| Set Query | Extended by using other commands as queries. |
| Device Trigger (DT) | Not implemented. |
| INIT Command | Causes the instrument to return to a default initialization state. |
| Time/Date Commands | Not implemented. |
| Stored Setting Commands | Not implemented. |
| Waveform Transmission | Implemented. Waveforms may be encoded in ASCII, HEX, or BINARY. The oscilloscope powers on with the encoding set to BINARY. |
| Remote On/Off | REMote must be set to ON to get the instrument to change a remote-controllable function. The instrument powers up with REMote OFF. |
| IEEE 728 | Compliance not intended. |

<!-- page 7-11 -->

### Table 7-8: Implementation of Specific Features for Option 12

| Feature | Choice Made | Comments |
|---|---|---|
| Secondary Addressing | Not implemented. | |
| Indicators | | |
| ADDR (carrier detect) | Implemented. | ADDR indicator comes on when carrier is detected. |
| SRQ (service request) | Implemented. | SRQ indicator is on only when a status byte is sent. |
| PLOT | Implemented | PLOT indicator is on when acquistions are locked out during a waveform plot. |
| Parameter Selection | Implemented | A ten-section switch sets the instruments's baud rate, data parity type, message terminator, and printer/plotter selections. Switch settings are read at power on only. |

**[Figure 7-3. Option 12 side panel — rear-panel diagram (drawing 6530-28) showing the AUXILIARY CONNECTOR (9-pin, pins labeled RELAY N.O., +4.2VDC, RELAY COMM, SIG GND, EXT CLK, RELAY N.C., X, Y, SHIELD GND), the 10-position PARAMETERS switch (numbered 1-10, positions 0/1), the RS-232-C PORT area with two 25-pin D-sub connectors labeled "RS232 DTE" and "RS232 DCE" (with a caution label "25Vpk AND <100mA ABS. MAX APPLIED TO ANY CONNECTOR"), captioned RS-232-C DTE CONNECTOR and RS-232-C DCE CONNECTOR.]**

### Table 7-9: RS-232-C DTE Connector

| Pin | Signal Name (Internal) | Signal Name (External) | Function |
|---|---|---|---|
| 1 | CHAS GND | CHAS GND | Chassis ground |
| 2 (a) | ITXD | TXD | Transmitted data |
| 3 (a) | IRXD | RXD | Received data |
| 4 | IRTS | RTS | Request to send |
| 5 | ICTS | CTS | Clear to send |
| 6 | IDSR | DSR | Data set ready |
| 7 (a) | SIG GND | SIG GND | Signal ground |
| 8 | IRLSD2 | RLSD | Received line signal detect |
| 20 | IDTR | DTR | Data terminal ready |

(a) These lines are all that are required for communication without hard control lines.

<!-- page 7-12 -->

**NOTE**

*Do not hook up external devices to both the DTE connector and the DCE connector at the same time.*

**RS-232-C DCE Connector**—Provides a connector that meets the EIA RS-232-2 standard for data communications equipment (see Figure 7-3). Table 7-10 lists the function of each pin of the connector. The connector is provided only on Option 12 instruments.

### Option 12 Interface Status Indicators

The three indicator labels (SRQ, ADDR, and PLOT) above the CRT indicate the status of the Communications interface. Refer to Figure 7-2 (shown previously) for the location of the status indicators. Their operation is as follows:

The SRQ indicator is on only during the time an asynchronous status byte is being sent. A status byte or event code is not generated for power-on. Events must be queried to receive pending events codes. Status must also be queried to receive pending status bytes, except for command and execution error status which are returned immediately upon recognition of an error. If OPC is also on, additional system events (i.e., warnings and operation complete) will also generate an asynchronous service request. All status bytes are prevented from reporting if RQS is off, but the SRQ indicator does not indicate that a status byte is pending. In this case, the event code must be queried (EVEnt?) to find out if an event has happened.

The ADDR indicator is on when a carrier is detected. With no devices connected to either the DTE port or the DCE port, the ADDR indicator will be on. If an RS-232-C DCE device is connected to the DCE port, the carrier will also be on all the time. The indicator will be off if a DTE device is connected to the DTE port and no carrier is detected.

The PLOT indicator is on when the communication option is currently sending waveform data. Acquisitions are inhibited during this time.

### Table 7-10: RS-232-C DCE Connector

| Pin | Signal Name (Internal) | Signal Name (External) | Function |
|---|---|---|---|
| 1 | CHAS GND | CHAS GND | Chassis ground |
| 2 (a) | IRXD | TXD | Transmitted data |
| 3 (a) | ITXD | RXC | Received data |
| 4 | ICTS | RTS | Request to send |
| 5 | IRTS | CTS | Clear to send |
| 6 | IDTR | DSR | Data set ready |
| 7 (a) | SIG GND | SIG GND | Signal ground |
| 8 | IRLSDC1 | RLSD | Received line signal detect |
| 20 | IDSR | DTR | Data terminal ready |

(a) These lines are all that are required for communication without hard control lines.

### RS-232-C Parameter Selection

Selection of RS-232-C parameters (baud rate, parity, and line terminator) must be made prior to power on using the RS-232-C PARAMETER switch and Table 7-11 through Table 7-13. Changes to the PARAMETER switch after power on will not be read until the next power on occurs. PARAMETERS switch settings and setups for some common printers and plotters are given in Appendix B. There are two other communications parameters that are set using commands via the interface itself. These are STOP bits and FLOW control. The most used setting for STOP is 1. The power-on default for FLOW is OFF.

**Baud Rate.** Baud rate switch settings determine the baud rate used by the instrument for both sending and receiving data. The available baud rates are listed in Table 7-12.

When OFF LINE (baud-rate switch settings 1111) is selected, the instrument still presents an active load to the other RS-232-C device, but it can't send or receive any interface traffic.

Use Table 7-11, Table 7-12, and the PARAMETERS switch to select the desired baud rate.

**Parity.** The selected parity settings determine the oscilloscope's response to received parity errors and the parity of data sent by the oscilloscope.

<!-- page 7-13 -->

### Table 7-11: RS-232-C PARAMETERS Switch

| Switch Section | Switch Position | Function |
|---|---|---|
| 1 | -- | Baud rate (a) |
| 2 | -- | Baud rate (a) |
| 3 | -- | Baud rate (a) |
| 4 | -- | Baud rate (a) |
| 5 | 0 | Parity is not checked. The data word is 8 bits long. |
| 5 | 1 | Parity is checked according to the settings of switches 6 and 7. A parity error causes a status byte to be sent if RQS is on. The data word is 7 bits long with the 8th bit being the parity bit. |
| 6 | | Parity select (b) |
| 7 | | Parity select (b) |
| 8 | 0 | Lines are terminated with carriage return (CR). |
| 8 | 1 | Lines are terminated with carriage return-line feed (CR-LF). |
| 9 | | Printer/Plotter selection (c) |
| 10 | | Printer/Plotter selection (c) |

(a) See Table 7-12.
(b) See Table 7-13.
(c) Switches 9 and 10 select printer/plotter devices at power up. The devices may be changed after power-up using Option commands or, in the case of the 2230, the MENU controls. Two EPSON formats are selectable. EPS7 uses seven print wires per head pass, and is usually slower. It is the chr$(27) "L" mode. EPS8 uses eight print wires per head pass, and is usually the faster print-head speed. It is the chr$(27) "Y" mode. In this mode, most Epson and Epson-compatible printers will not strike any print wire more often than every second pixel. EPS8 is selected when parity is disabled. Printing/plotting devices are selected with the following switch positions:

| Switch 9 | Switch 10 | Device Selected |
|---|---|---|
| 0 | 0 | HP-GL(R) plotter |
| 1 | 0 | Epson(R) (EPS7 or EPS8) |
| 0 | 1 | ThinkJet(R) printer |
| 1 | 1 | X-Y Plotter |

HP-GL(R) and ThinkJet(R) are trademarks of Hewlett-Packard Company. Epson(R) is a trademark of Epson Corporation.

Section 5 of the PARAMETERS switch determines whether or not received parity errors will cause an error report (see Table 7-11). With parity enabled, seven bits represent the characters being sent. The eighth bit is the parity bit, and is interpreted as selected by the settings of switches 6 and 7. These sections of the PARAMETERS switch determine the parity used when transmitting and receiving data over the RS-232-C interface. ODD, EVEN, MARK, or SPACE parity is selectable (see Table 7-13).

By setting both the transmitting and receiving devices to use parity, some degree of checking may be done on 7-bit data. Setting parity to "even" causes the transmitter to send a parity bit that makes the number of "mark" bits in the data (plus the parity bit) come out to an even number. Upon receiving the data, the receiving device adds up the "mark" bits in the data byte. If an error is detected, a system event status byte is sent. When the event code byte is interpreted, the controller may make a hardware change or alter its routine to handle the error.

"Odd" parity works in the same way, except that the number of "mark" bits is expected to be odd. Parity may also be set to "mark" or "space" where the parity bit is always set to a mark or a space respectively.

### Table 7-12: Baud Rate

| Switch Position (4 3 2 1) | Baud Rate |
|---|---|
| 0 0 0 0 | 50 |
| 0 0 0 1 | 75 |
| 0 0 1 0 | 110 |
| 0 0 1 1 | 134.5 |
| 0 1 0 0 | 150 |
| 0 1 0 1 | 300 |
| 0 1 1 0 | 600 |
| 0 1 1 1 | 1200 |
| 1 0 0 0 | 1800 |
| 1 0 0 1 | 2000 |
| 1 0 1 0 | 2400 |
| 1 0 1 1 | 3600 |
| 1 1 0 0 | 4800 |
| 1 1 0 1 | 7200 |
| 1 1 1 0 | 9600 |
| 1 1 1 1 | Off Line |

<!-- page 7-14 -->

### Table 7-13: Parity Selection (a)

| Switch Position (6 7) | Parity Type | Comment |
|---|---|---|
| 0 0 | ODD | The parity bit of each byte is set or cleared as needed to make the number of logical ones per word byte odd. |
| 1 0 | EVEN | The parity bit of each byte is set or cleared as needed to make the number of logical ones per word byte even. |
| 0 1 | MARK | The parity bit is always set to a logical one. |
| 1 1 | SPACE | The parity bit is always cleared to a logical zero. |

(a) Characters are always accepted if possible. If parity is enabled and RQS is on, a status byte is sent if the received parity doesn't match the parity selected. Parity must be disabled (PARAMETERS switch position 5 set to 0 before power on) for binary data transfers.

**Message Line Terminator.** PARAMETERS switch section 8 selects the line terminator. The line terminator is either CR (carriage return), with switch section 8 open, or CR-LF (carriage return and line feed), with switch section 8 closed (see Table 7-11).

**NOTE**

*Commands to the oscilloscope are interpreted and carried out as soon as they are recognized as such; the oscilloscope does not wait for a CR or CR-LF to end the command string. If a command needs to be correctly done before the next command is sent, the controller must wait for the correct return. If an error occurs (due to command syntax or incompatible instrument settings), the error status will be immediately reported. The controller can detect the error, query the event code, and take corrective action before going on with another command that may not be handled properly. This is especially true if the previous command puts the oscilloscope in a state that prevents it from responding. For this reason, the recommended practice is to send only one command in each message line to the oscilloscope.*

When CR (normal mode) is selected as the terminator, the instrument will:

- Accept only CR as the line terminator.
- Send CR as the last byte of a message.

When CR-LF is selected as the terminator, the instrument will:

- Accept either CR-LF or LF only as the line terminator.
- Send CR-LF (carriage return followed by line feed) at the end of every message.

### STOP Bits

Once communication is established between the controller and the oscilloscope, commands may be sent to the oscilloscope. When dealing with the transfer of data via the RS-232-C interface, the bits used to make up a character consist of a start bit, seven or eight data bits, and, finally, one or two stop bits. Start and stop bits separate the data bytes and are called framing pulses. The start bit is always set to a "mark," and the one or two stop bits are set to a "space." One stop bit is used in most applications. Two stop bits may be needed for some printers at some baud rates. The command STOP 1 or STOP 2 sets the number of stop bits in the character frame.

**NOTE**

*For the 2220 and 2221 instruments, selection of the stop bits is not possible from the front-panel controls. When connecting to a printer or plotter with a choice of stop bits for different baud-rate settings, select a baud rate that requires only one stop bit.*

The transition from one character's stop bit(s) to the next character's start bit is used to synchronize the receiver to the transmitter. This causes the coded data bits for each character to be read at the best time relative to the start of the character's start.

Errors that occur due to mismatched baud rates, data bits, or stop bits show up as "framing errors." The start-bit and stop-bit frame surrounding the character bits have the wrong timing relationship with respect to each other. Since they are not recognized properly, the data stream cannot be interpreted by the receiving device.

<!-- page 7-15 -->

### FLOW Control

When transmitting data using modems to interconnect two devices via the telephone lines, the normal handshaking lines are not used. The two devices can still communicate using a data-transmission technique called "flow control." Using this method, the data sent can be separated from non-data being received (such as noise). This is done by interpreting every correctly framed data pattern as a valid character and constantly checking for two specific characters that turn the transmission on and off.

These flow-control characters are called XON (transmission on) and XOFF (transmission off). The usual assignment for these is {control-Q} for XON and {control-S} for XOFF, though the specific characters chosen are a function of the communications program used. When communicating over telepnone lines, flow control greatly increases the chance that ASCII or HEX encoded data will be correctly transferred.

The FLOW ON command allows the oscilloscope some on/off control of the data transfer. At power-on, the default data encoding is BINARY. Flow control can not be used for the transmission of binary-encoded waveform data, so the power-on setting of FLOW is set to off. Before sending binary-encoded data, FLOW OFF must be sent if flow control was previously set ON. The Advanced Functions menu of the 2230 also has a menu choice for setting flow control.

### Remote-Local Operating States

The following paragraphs describe the two operating states of the instrument: Local and Remote.

**REMOTE OFF (LOCAL)**—With REMOTE OFF, instrument settings are controlled manually by the operator using the front-panel controls. Option interface messages such as REMOTE ON, RQS ON, and OPC ON are received and executed. Queries about instrument's states or measurement results will be answered. Device-dependent commands that require an instrument operating mode change to be made cause an execution error, and a service request will be generated if RQS is on.

**REMOTE ON (REMOTE)**—In this state, the oscilloscope executes all commands sent to it. Remote-controllable front-panel indicators and CRT readouts are updated as commands are carried out. There is no local lockout (LLO). Changing any option-controllable front-panel setting locally overrides the remote settings. If a waveform is being transmitted, the PLOT indicator will be lit, and new waveform data will not be acquired until the transmission is done.

### Reset Under Communication Option Control

Certain default settings for acquisition and plot modes may be set up sending the INIt command. The INIt command does not invoke the power-up test. Upon completion of the INIt command, no status byte or event code is generated.

The default settings are as follows:

- ACQUISITION REP:AVE
- ACQUISITION HSREC:SAMPLE
- ACQUISITION LSREC:PEAKDET
- ACQUISITION SCAN:PEAKDET
- ACQUISITION ROLL:PEAKDET
- ACQUISITION SMOOTH:ON
- ACQUISITION WEIGHT:4 (16 in the 2220 and 2221)
- ACQUISITION NUMSWEEPS:0
- ACQUISITION VECTORS:ON
- DATA ENCDG:BINARY
- DATA SOURCE:ACQ
- DATA TARGET:REF1 (REF4 in the 2220 and 2221)
- PLOT GRAT:OFF
- PLOT FORMAT:{power-on setting}
- READOUT ON
- Menu system reset.

## RS-232-C PROGRAMMING

Things to consider when writing programs for your RS-232-C controller are given here to help you when you must develop your own interfacing software. Before a program can be used to control the oscilloscope, the RS-232-C communication parameters for baud rate, line terminator, and parity must be set. Settings for these parameters are selected and set using the RS-232-C PARAMETERS switch found on the side panel of the oscilloscope.

Controller programs are usually composed of two main parts or routines. The two parts are generally called the command handler and the service-request handler.

<!-- page 7-16 -->

**COMMAND HANDLER**—Basically, a command handler establishes communication between the controller and the oscilloscope, sends commands to the oscilloscope, receives responses from the oscilloscope, and displays the responses as required. The steps of the following procedure are the general functions that the command-handler software routine should be able to do for the most useful communications.

1. Initialize the controller in the communications mode.
2. Watch for a service request.
3. Check the event code (by sending an EVEnt? query) if a service request occurs.
4. Determine the action needed to be taken from the event code byte that is returned and take it.
5. Get a command to send to the oscilloscope.
6. Send a command to the oscilloscope.
7. Check for a response from the oscilloscope.
8. If the response is an error status, check the event code (Step 3) and take the appropriate action (Step 4).
9. Repeat Steps 5 through 8 as many times as needed.

**SERVICE REQUEST HANDLER**—The service-request handler routine should contain the necessary instructions to process the possible event codes generated by the 2200 Family DSO. The 2200 Family DSO requests service by sending asynchronous status bytes when certain errors occur (if RQS is ON). Other status bytes return as the result of a STAtus? query, or when OPC is on. The immediate mode service request may cause the controller to halt unless the controller's program is written to properly handle them. A user may also want the controller routine to be able to recognize and handle the other events requiring service. These events are identified in Tables 7-34 and 7-35 at the back of this section.

The following general steps are required to handle service requests from the oscilloscope.

1. Watch for an asynchronous service-request status byte. This is the same concept as checking for an SRQ with the GPIB controller program.
2. Send an EVEnt? query to obtain the event-code byte that describes in more depth what caused the service request.
3. If the response to the EVEnt? query is not zero, perform the action required to handle the event.
4. Return to the main program.

### Option 12 Status and Error Reporting

The status and error reporting system used by the Communication Option sends status bytes that may be viewed as a service request when monitored by the appropriate controller software. As soon as a change of status or an error occurs, the 2200 Family instrument returns a service request status byte that indicates the type of event that occurred (if RSQ is on). The status byte returned and the event code returned as the reply to an EVEnt? query provide a limited amount of information about the specific cause of the service-request status-byte. Command errors, execution errors, and internal errors generate a service-request status byte immediately (if RQS is ON). To retrieve other system-event and warning status bytes, OPC must be ON, and the oscilloscope must be queried by the STAtus? command. See Tables 7-34 and 7-35 at the back of this section for status-byte and event codes.

<!-- page 7-17 -->

## COMMUNICATION AND WAVEFORM TRANSFER

This subsection contains information common to both Option 10 and Option 12. The commands available, the command protocol, waveform transfer information, and the service request status bytes are included in this subsection.

### READOUT/MESSAGE COMMAND CHARACTER SET

Character translations performed by the MESsage command and query, when sending data to or receiving data from the CRT readout, are indicated in Table 7-14. The standard ASCII character codes are given in Table 7-15.

**NOTE**

*Values in Table 7-14 that have no CRT equivalent are translated into spaces when sent to the display.*

In the Command tables found at the end of this section, headers and arguments are listed in a combination of upper-case and lower-case characters. The instrument accepts abbreviated headers and arguments that contain at least the upper-case characters shown in the tables (whether sent in upper case or lower case). The lower-case characters may be added to the abbreviated (upper case) version, but they can only be those shown in lower case. For a query, the question mark must immediately follow the header. For example, any of the following formats are acceptable to the oscilloscope:

VMO? or vmo?
VMOd? or vmod?
VMOde? or vmode?

HEADERS—A command consists of at least a header. Each command has a unique header, which may be all that is needed to invoke a command; for example:

INIt
OPC

ARGUMENTS—Some commands require the addition of arguments to their headers to describe exactly what is to be done. If there is more to the command than just the header (including the question mark if it is a query), then the header must be followed by at least one space.

In some cases, the argument is a single word; for example:

REFTo REF4
PLOt STArt

In other cases, the argument itself requires another argument. When a second argument, or "link argument," is required, a colon must separate the two arguments. Two examples of this are:

ACQuisition REPetitive:SAMple

and

WFMpre XINcr:1.0E-3

### MESSAGES AND COMMUNICATION PROTOCOL

The commands available to the user via either the Option 10 GPIB or the Option 12 RS-232-C communications option can set some of the instrument's digital storage operating modes, query the results of measurements made, or query the state of the oscilloscope. The commands are specified in mnemonics that are related to the functions implemented. For example, the command INIt initializes instrument settings to states that would exist if the instrument's power was cycled. To further facilitate programming, command mnemonics are similar to front-panel control names.

**NOTE**

*All measurement results returned by the options have the same accuracy as the main instrument.*

### Commands

Commands for this instrument, like those for other Tektronix instruments, follow the conventions established in a Tektronix Codes and Formats Standard. The command words were chosen to be as understandable as possible, while still allowing a user familiar with the commands to reduce the number of key strokes needed and still have the command unambiguous. Syntax is also standardized to make the commands easier to learn.

<!-- page 7-18 -->

**[Table 7-14: Readout/MESage Command Character Set — a 16x8 code-page chart (drawing 6530-29) mapping 7-bit codes 0-127 (octal/decimal dual-labeled, columns grouped as CONTROL (bits B7 B6 B5 = 000/001), SYMBOLS (010/011), UPPERCASE (100/101), LOWERCASE (110/111)) to the readout's custom character set. Mostly matches standard ASCII (digits, punctuation, A-Z, a-z) but substitutes several control-code positions (0x00-0x1F) with special stroke-font symbols not representable in plain text: position 2 = "BWL" stacked glyph, 3 = a boxed-X symbol, 4 = a delta (triangle), 5 = an en-dash-like bar, 6 = double-equals, 7 = a grounded-triangle/antenna symbol, 14 = "Hz" glyph, 15 = a stacked fraction-like "1/Δ" glyph, and position 21 (row "0 1 0", col 1) = tilde-V, 20 = a down-arrow, 23 = a micro (μ) sign. This is the CRT readout's own symbol table for the MESsage command - potentially directly relevant to this project's own stroke-font/readout character-set findings, see docs/display/vector-display-and-stroke-font.md.]**

<!-- page 7-19 -->

**[Table 7-15: ASCII Code Chart — standard 7-bit ASCII code chart (drawing 6530-30) in the classic GPIB-manual layout: control codes 0-31 (with GPIB mnemonics like GTL/LLO/SDC/GET/TCT alongside the standard ASCII control mnemonics NUL/SOH/STX/etc.), numbers/symbols 32-63, upper case 64-95, lower case 96-127, each cell showing octal, ASCII mnemonic/character, and decimal. Columns are also labeled by GPIB role: ADDRESSED COMMANDS, UNIVERSAL COMMANDS, LISTEN ADDRESSES, TALK ADDRESSES, SECONDARY ADDRESSES OR COMMANDS (PPE/PPD). This is the standard 7-bit ASCII table, not 2230-specific - included for completeness since the manual references it directly from Table 7-14 above.]**

<!-- page 7-20 -->

Where a header has multiple arguments, the arguments (or argument pairs, if the argument has its own argument) must be separated by commas. Two examples of this syntax are:

DATa ENCdg:BINary,CHAnnel:CH2

and

VMOde? CH1,CH2,ADD

**NOTE**

*With Option 12, multiple commands (especially queries) should not be used in a single programmed message line. Commands (and arguments to commands) are interpreted and acted on by the oscilloscope as soon as a separator is recognized; the oscilloscope does not wait for the message terminator (CR or CR-LF) to signal the end of the command line. If one of the commands in a command line requires a response for any reason (i.e., command error, illegal command, or unable to do the command), the oscilloscope's service-request status-byte response will be asynchronously sent. If the service request is not handled correctly, the controller may not be able to continue with its program.*

COMMAND SEPARATOR—Multiple commands may be put into one command line by separating the individual commands with a semicolon; for example:

DATa ENCdg:BINary,CHAnnel:CH2;WFMpre XINcr:1.0E-3

Multiple commands in a message are not recommended with RS-232-C controller routines for Option 12. See the previous NOTE. However, the command separator is valid, and multiple commands on the same message line may be used. A waveform preamble is one example of using multiple commands in a single message. With Option 10, GPIB controller programs often use multiple commands in a single line.

GPIB MESSAGE TERMINATOR—As previously explained, GPIB messages may be terminated with either EOI or LF. Some controllers assert EOI concurrently with the last data byte; others use only the LF character as a terminator. The GPIB interface can be set to accept either terminator. With EOI selected, the instrument interprets a data byte received with EOI asserted as the end of the input message; it also asserts EOI concurrently with the last byte of an output message. With the LF setting, the instrument interprets the LF character without EOI asserted (or any data byte received with EOI asserted) as the end of an input message; it transmits a Carriage Return character followed by Line Feed (LF with EOI asserted) to terminate messages.

RS-232-C MESSAGE TERMINATOR—RS-232-C messages from the oscilloscope may be terminated with either carriage return (CR) or the CR and line-feed (LF) characters. The RS-232-C Option can be set to send and receive either terminator as the last byte of a message. The instrument does not wait for the end-of-line terminator when it handles incoming messages. It recognizes a semicolon as the end of command terminator and immediately begins its response to the preceding command string. Because of the way the instrument handles commands, messages should be limited to one command per line. Incoming and outgoing messages are not stacked. If more than one command per line is sent, responses to the first commands in a line may be lost when the output buffer is reinitialized to output the response to the last command in a line. Even single command messages should not be terminated twice. The response to the command may be lost when the instrument sees the second terminator.

COMMAND FORMATTING—Commands sent to the oscilloscope must have the proper format (syntax) to be understood; however, this format is flexible in that many variations are acceptable. The following paragraphs describe this format and the acceptable variations.

The oscilloscope expects all commands to be encoded as either upper-case or lower-case ASCII characters. All data output is in upper case.

Spaces can be used as formatting characters to enhance the readability of command sequences. As a general rule, spaces can be placed either after commas and semicolons or after the space that follows a header.

NUMERIC ARGUMENTS—Table 7-16 shows the number formats for the {NR1}, {NR2}, and {NR3} arguments used in a command. Both signed and unsigned numbers are accepted, but unsigned numbers are taken as positive.

<!-- page 7-21 -->

### Table 7-16: Numeric Argument Format for Commands

| Numeric Argument | Number Format | Examples |
|---|---|---|
| {NR1} | Integers | +1, 2, -1, -10 |
| {NR2} | Explicit decimal point | -3.2, +5.1, 1.2 |
| {NR3} | Floating point in scientific notation | +1.E-2, 1.0E+2, 1.E-2, 0.02E+3 |

## WAVEFORM TRANSFERS

The instrument can transmit and receive waveforms. It can transfer these waveforms in binary, hexadecimal, or ASCII encoding. When sending waveforms to the instrument, the target must be one of the numbered reference memories (REF4 only for the 2220 and 2221). Waveforms transferred from the oscilloscope to the controller may be from either the current acquisition or one of the numbered reference memories (again REF4 for the 2220 and 2221). The data source (the memory location from which the waveform data comes) and the data target (the memory location where data sent to the oscilloscope ends up) are selected independently.

### Waveform Preamble

The waveform preamble contains the attributes for the associated waveform data. These attributes include the number of points per waveform, scale factors, vertical offsets, horizontal increment, scaling units, and data encoding. The preamble information is sent as an ASCII-encoded string in all cases. The exact attributes sent depend on the waveform and the acquisition mode.

A typical response to the preamble query WFMpre? for a Y (time-implied) acquisition is:

```
WFM WFI:"ACQ, CH1,0.5V,DC,0.2mS,SAMPLE,
CRV# 1",NR.P:4096,PT.O:122,PT.F:Y,
XMU:0.0E0,XOF:0,XUN:S,XIN:2.0E-6,
YMU:20.0E-3,YOF:-20,YUN:V,ENC:HEX,BN.F:RP,
BYT:1,BIT:8,CRV:CHK;
```

A typical response to the preamble query for an X-Y acquisition is:

```
WFM WFI:"ACQ,XY,0.2V,DC,50.0mV,DC,
1.0uS, SAMPLE, CRV# 4",
NR.P:2048,PT.O:216,PT.F:XY,XMU:8.0E-3,
XOF:0,XUN:S,XIN:20.0E-9,YMU:2.0E-3,YOF:0,
YUN:V,ENC:BIN,BN.F:RP,BYT:1,BIT:8,CRV:CHK;
```

These replies are single line messages that end with the selected message terminator (CR or CR-LF). With the GPIB interface, EOI (end-or-identify) is also sent if that terminator mode is selected.

### Transferring Waveforms

The oscilloscope can respond with the preamble only, the curve data only, or the preamble and curve data together. The queries to obtain these responses are, in order, WFMpre?, CURVe?, and WAVfrm?

For the combined response to WAVfrm?, the preamble is separated from the curve data by a semicolon (;).

The preamble information is always formatted as ASCII characters. Waveform (CURVE) data internal to the oscilloscope is stored as 8-bit, unsigned integers. Before that data is sent via the Communications option, it is changed into one of three formats: binary, hexadecimal, or ASCII. The resolution of the formatted data points may be either 8-bit or 16-bit. Waveform record length is 1024 data points for the shortest or 4096 data points for the longest. The number of bytes that are required to transfer data depends on several variables. See the NR.Pts description in the Waveform Preamble Fields command table for more information. The largest number of curve data bytes ever needed to send a waveform is 8192 bytes (for a 4K record that has two bytes per data point).

### Binary Encoding

BINary data is transferred as unsigned binary integers. Each data point in the record is either 8 bits or, when averaged, 16 bits. BINary encoding format has the following waveform curve data form:

<!-- page 7-22 -->

```
CURVE {space} % {Binary Count MSB} {Binary Count LSB} {Binary Data} {Checsum} {Terminator}
```

Where:

| Field | Description |
|---|---|
| CURVE | is a literal string indicating that curve data follows. |
| % | is used as a header character to show the start of a binary block. |
| {Binary Count MSB} | is the most-significant byte of the two-byte Binary Count. Binary Count is the length of the waveform, in bytes, plus the one-byte checksum. |
| {Binary Count LSB} | is the least-significant byte of the Binary Count. |
| {Binary Data} | is made up of 256, 512, 1024, 2048, or 4096 data points. Each data point is either a 1-byte (8 bits) or 2-byte (16 bits) representation of each digitized value. |
| {Checksum} | is the two's-complement of the modulo 256 sum of the preceding data bytes and the binary count. The Checksum is used by the controller program to verify that all data values have been received correctly. |

Table 7-17 illustrates the data transferred for a 4096-point, 8-bit, binary-encoded waveform. The waveform data-point values vary with the signal amplitude.

Table 7-18 illustrates the data transferred for a 4096-point, 16-bit (averaged), binary-encoded waveform.

### Hexadecimal Encoding

With HEXadecimal waveform data encoding, characters representing an 8-bit or 16-bit data point are sent in a fixed ASCII hexadecimal format. There are no delimiters (commas) between data points. Data format is very similar to BINary format, with the following exceptions:

1. The curve header is "CURVE #H" instead of "CURVE %".

2. Each data point is two ASCII hexadecimal characters for 8-bit transfers and four ASCII hexadecimal characters for 16-bit transfers.

3. The byte count is sent as four successive ASCII hexadecimal characters, but the value of the byte count is identical to a comparable BINary transfer.

4. The checksum is sent as two successive ASCII hexadecimal characters.

### Table 7-17: Typical 8-Bit Binary-Encoded Waveform Data

| Byte | Contents | Decimal | GPIB EOI (1=Asserted) |
|---|---|---|---|
| 1 | C | 67 | 0 |
| 2 | U | 85 | 0 |
| 3 | R | 82 | 0 |
| 4 | V | 86 | 0 |
| 5 | E | 69 | 0 |
| 6 | {SP} | 32 | 0 |
| 7 | % | 37 | 0 |
| 8 | {Bin Count MSB} | 16 (a) | 0 |
| 9 | {Bin Count LSB} | 01 (a) | 0 |
| 10 | 1st Pt | d1 | 0 |
| 11 | 2nd Pt | d2 | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| 4105 | 4096th Pt | d4096 | 0 |
| 4106 | {Checksum} | chk | 1 When TERM=EOI |
| 4107 (b) | {CR} | 13 | 0 |
| 4108 (c) | {LF} | 10 | 1 |

(a) (1001 hex 16, or 4097 decimal 10)
(b) All RS-232-C or GPIB with TERM = LF/EOI.
(c) RS-232-C with TERM = CR-LF.

<!-- page 7-23 -->

### Table 7-18: Typical 16-Bit Binary-Encoded Waveform Data

| Byte | Contents | Decimal | GPIB EOI (1=Asserted) |
|---|---|---|---|
| 1 | C | 67 | 0 |
| 2 | U | 85 | 0 |
| 3 | R | 82 | 0 |
| 4 | V | 86 | 0 |
| 5 | E | 69 | 0 |
| 6 | {SP} | 32 | 0 |
| 7 | % | 37 | 0 |
| 8 | {Bin Count MSB} | 32 (a) | 0 |
| 9 | {Bin Count LSB} | 01 (a) | 0 |
| 10 | 1st Pt MSB | d1H | 0 |
| 11 | 1st Pt LSB | d1L | 0 |
| 12 | 2nd Pt MSB | d2H | 0 |
| 13 | 2nd Pt LSB | d2L | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| 8200 | 4096th Pt MSB | d4096H | 0 |
| 8201 | 4096th Pt LSB | d4096L | 0 |
| 8202 | {Checksum} | chk | 1 When TERM=EOI |
| 8203 (b) | {CR} | 13 | 0 |
| 8204 (c) | {LF} | 10 | 1 |

(a) (2001 hex 16, or 8193 decimal 10)
(b) All RS-232-C or GPIB with TERM = LF/EOI.
(c) RS-232-C with TERM = CR-LF.

Tables 7-19 and 7-20 illustrate 8-bit and 16-bit HEXadecimal-encoded waveform-data transfers.

### ASCII Encoding

With ASCII waveform data encoding, ASCII characters representing the binary value of each waveform data point are sent in variable length format, separated by commas. In ASCII format, the curve data transfer is represented as:

```
CURVE{space}data,data,data,.....,data{terminator}
```

### Table 7-19: Typical 8-Bit Hexadecimal-Encoded Waveform Data

| Byte | Contents | Decimal | GPIB EOI (1=Asserted) |
|---|---|---|---|
| 1 | C | 67 | 0 |
| 2 | U | 85 | 0 |
| 3 | R | 82 | 0 |
| 4 | V | 86 | 0 |
| 5 | E | 69 | 0 |
| 6 | {SP} | 32 | 0 |
| 7 | # | 35 | 0 |
| 8 | H | 72 | 0 |
| 9 | {Bin Count MS 4 bits} | 49 | 0 |
| 10 | . | 48 | 0 |
| 11 | . | 48 | 0 |
| 12 | {Bin Count LS 4 bits} | 49 | 0 |
| 13 | 1st Pt MS 4 bits | d1H | 0 |
| 14 | 1st Pt LS 4 bits | d1L | 0 |
| 15 | 2nd Pt MS 4 bits | d2H | 0 |
| 16 | 2nd Pt LS 4 bits | d2L | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| 203 | 4096th Pt MS 4 bits | d4096H | 0 |
| 204 | 4096th Pt LS 4 bits | d4096L | 0 |
| 205 | {Checksum MS 4 bits} | chkH | 0 |
| 206 | {Checksum LS 4 bits} | chkL | 1 When TERM=EOI |
| 207 (a) | {CR} | 13 (if term=LF/EOI) | 0 |
| 208 (b) | {LF} | 10 (if term=CR-LF) | 1 |

(a) All RS-232-C or GPIB with TERM = LF/EOI.
(b) RS-232-C with TERM = CR-LF.

<!-- page 7-24 -->

### Table 7-20: Typical 16-Bit Hexadecimal-Encoded Waveform Data

| Byte | Contents | Decimal | GPIB EOI (1=Asserted) |
|---|---|---|---|
| 1 | C | 67 | 0 |
| 2 | U | 85 | 0 |
| 3 | R | 82 | 0 |
| 4 | V | 86 | 0 |
| 5 | E | 69 | 0 |
| 6 | {SP} | 32 | 0 |
| 7 | # | 35 | 0 |
| 8 | H | 72 | 0 |
| 9 | {Bin Count MS 4 bits} | 50 | 0 |
| 10 | . | 48 | 0 |
| 11 | . | 48 | 0 |
| 12 | {Bin Count LS 4 bits} | 49 | 0 |
| 13 | 1st Pt MS 4 bits | d1H | 0 |
| 14 | . | . | 0 |
| 15 | . | . | 0 |
| 16 | 1st Pt LS 4 bits | d1L | 0 |
| 17 | 2nd Pt MS 4 bits | d2H | 0 |
| 18 | . | . | 0 |
| 19 | . | . | 0 |
| 20 | 2nd Pt LS 4 bits | d2L | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| 6393 | 4096th Pt MS 4 bits | d4096H | 0 |
| 6394 | . | . | 0 |
| 6395 | . | . | 0 |
| 6396 | 4096th Pt LS 4 bits | d4096L | 0 |
| 6397 | {Checksum MS 4 bits} | chkH | 0 |
| 6398 | {Checksum LS 4 bits} | chkL | 1 When TERM=EOI |
| 6399 (a) | {CR} | 13 (If term=LF/EOI) | 0 |
| 6400 (b) | {LF} | 10 (If term=LF/EOI) | 1 |

(a) All RS-232-C or GPIB with TERM = LF/EOI.
(b) RS-232-C with TERM = CR-LF.

### Table 7-21: Typical ASCII-Encoded Waveform Data

| Byte | Contents | Decimal | GPIB EOI (1=Asserted) |
|---|---|---|---|
| 1 | C | 67 | 0 |
| 2 | U | 85 | 0 |
| 3 | R | 82 | 0 |
| 4 | V | 86 | 0 |
| 5 | E | 69 | 0 |
| 6 | {SP} | 32 | 0 |
| 7 | Pt100(1) (a) | d100(1) | 0 |
| 8 | Pt10(1) (a) | d10(1) | 0 |
| 9 | Pt1(1) (a) | d1(1) | 0 |
| 10 | . | 44 (b) | 0 |
| . | . | . | 0 |
| . | . | . | 0 |
| XXX | Pt100(4096) (a) | d100(4096) | 0 |
| XXX | Pt10(4096) | d10(4096) | 0 |
| XXX | Pt1(4096) (a) | d1(4096) | 0 |
| XXX (c) | {CR} | 13 | 0 |
| XXX (d) | {LF} | 10 | 1 |

(a) Each value sent may consist of from 1 to 3 characters. The notation Pt100 means "the hundreds digit", and Pt10 means "the tens digit", which may or may not be sent, depending on the magnitude of the value.
(b) The decimal value 44 equates to the comma sent between each successive value.
(c) All RS-232-C or GPIB with TERM = LF/EOI.
(d) RS-232-C with TERM = CR-LF.

## COMMUNICATION COMMANDS

Tables 7-22 through 7-33 describe all commands available for the 2200 Family Digital Storage Oscilloscopes equipped with either Communications option. The Commands column lists the complete command with header and argument(s). Multiple link arguments are enclosed in angle brackets ({link1, link2, or link3}). Numeric value arguments are also enclosed in angle

<!-- page 7-25 -->

brackets ({NR1}). Default arguments are enclosed in square brackets ([default]). Default arguments may be omitted from the command if that is the mode you want. The 2200 Family DSO for which the command is valid is identified immediately above the command. ALL indicates that the command is valid for all 2200 Family DSO intruments. Commands that are valid only for specific 2200 Family instruments are so indicated.

The capital letters shown are the fewest number of characters that identify the command as unique. They are also the letters returned by the oscilloscope with LONG OFF. Those letters shown in lower case are optional in the command. With LONG ON, all the letters of query return will be returned. All responses to queries are returned in upper case. The second column of the command tables gives a complete description of the command operation.

With GPIB, one or more arguments, separated by commas, may be given in a query to request only the information wanted rather than sending separate commands for each query. An example of this type of command is as shown:

CH1? VOLts,COUpling;

With RS-232-C, program your controller routines to send only one command at a time with single arguments of the form:

header argument:link argument;

This allows the controller to handle any asynchronous service request that may be generated by a command before attempting a second command.

### Command Tables

Instrument commands are presented in tables divided into the following functional groups:

| Table | Command Group |
|---|---|
| 7-22 | Vertical Commands |
| 7-23 | Horizontal Commands |
| 7-24 | Trigger Commands |
| 7-25 | Cursors Commands |
| 7-26 | Display Commands |
| 7-27 | Acquisition Commands |
| 7-28 | Save and Recall References Commands |
| 7-29 | Waveforms Commands |
| 7-30 | Waveform Preamble Fields |
| 7-31 | Miscellaneous Commands |
| 7-32 | Service Request Group Commands |
| 7-33 | RS-232-C Specific Commands |

<!-- page 7-26 -->

### Table 7-22: Vertical Commands

| Commands | Description |
|---|---|
| **2221 and 2230**<br />CH1? | Query only. Returns the present CH1 settings: CH1 VOL:{NR3}, COU:{AC, DC, or GND}. {NR3} is the VOLTS/DIV setting. |
| **2221 and 2230**<br />CH1? VOLts | Query only. Returns the CH1 VOLTS/DIV setting (including the probe attenuation factor). The value returned is a {NR3} number. For example, if the VOLTS/DIV setting is 50 mV, the value returned is CH1 VOL:5.0E-2. An execution warning is generated if the VOLTS/DIV CAL knob is not in the detent (calibrated) position. |
| **2221 and 2230**<br />CH1? COUpling | Query only. Returns the present CH1 input coupling: COU:{AC, GND, or DC}. |
| **2221 and 2230**<br />CH2?<br />CH2? VOLts<br />CH2? COUpling | Queries for CH2 the same as for CH1. |
| **2221 and 2230**<br />CH2? INVert | Query only. Returns CH2 INV:{ON or OFF}. |
| **ALL**<br />VMOde? | Query only. Returns the vertical mode setting: VMO:{CH1, CH2, ADD, CHOp, ALT, or XY}. |
| **2221 and 2230**<br />PROBe? {CH1 or CH2} | Query only. Returns the probe attenuation coding of the queried channel: CH{1 or 2} PROB:{NR1}. {NR1} may be 1000, 100, 10, 1, -1, or -2. The -1 value is for identify, and the -2 value is for unknown probe coding. |

<!-- page 7-27 -->

### Table 7-23: Horizontal Commands

| Commands | Description |
|---|---|
| **2230**<br />DELAy? | Returns the present horizontal delay settings as: DELA VAL:{NR3},UNI:{S or DIV}. |
| **2230**<br />DELAy? VALue | Returns an {NR3} value that represents the present delay value in the units returned by the UNIts query as: DELA VAL:{NR3}. |
| **2230**<br />DELAy? UNIts | Returns a string of either S or DIV that corresponds to the DELAy? VALue units as: DELA UNI:{S or DIV}. The units are DIV when the SEC/DIV knob is set to EXT CLK. |
| **ALL**<br />HORizontal? | Returns all present horizontal settings as appropriate for the type of instrument. |
| **ALL**<br />HORizontal? ASEdiv | Returns an {NR3} value that represents the present A SEC/DIV setting in the form: HOR ASE:{NR3}. The value returned is zero when the SEC/DIV knob is set to EXT CLK. |
| **2230**<br />HORizontal? BSEdiv | Returns an {NR3} value that represents the present B SEC/DIV setting in the form: HOR BSE:{NR3}. |
| **ALL**<br />HORizontal? EXTclk | Returns the state of the external clock as: HOR EXT:{ON or OFF}. |
| **ALL**<br />HORizontal? HMAg | Returns the state of the X10 magnifier as: HOR HMA:{ON or OFF}. |
| **2230**<br />HORizontal? MODe | Returns the present horizontal mode setting as: HOR MOD:{ASW, AIN, or BSW}. |

<!-- page 7-28 -->

### Table 7-24: Trigger Commands

| Commands | Description |
|---|---|
| **ALL**<br />ATRigger? [MODe] | Returns the present A trigger mode in the form: ATR MOD:{NOR, PPA, or SGL};. PPA is returned for both Peak-to-Peak Auto and TV Field trigger modes. The reply is the same with or without the optional [MODe] argument. |
| **ALL**<br />SGLswp ARM | Rearms a completed single sweep. An execution error is generated if the instrument is not in SGL SWP mode, and an execution warning is generated if the single sweep is already armed. With OPC ON, a service request status byte for operation complete is generated when the single sweep occurs. |
| **ALL**<br />SGLswp? | Returns the state of the SGL SWP trigger mode as: SGL {ARM or DON}; when SGL SWP trigger mode is on. If SGL SWP trigger mode is not on, a reply of "SGL ;" is made, and an execution warning is generated. |
| **ALL**<br />TRIggerd? | Returns the present state of the TRIG'D indicator as: TRI {ON or OFF};. |

<!-- page 7-29 -->

### Table 7-25: Cursor Commands

| Commands | Description |
|---|---|
| **2221 and 2230**<br />CURSor CHAnnel:{CH1-CH2} | Selects the named channel as the channel from which the cursor voltage difference is returned by the DELTAV? query. No warning is generated if the cursors are directed to an undisplayed channel. |
| **2221 and 2230**<br />CURSor POSition:{NR1} | Selects the horizontal data point position of the active cursor. If the acquisition is a 1-Kbyte record and the position requested is past 1023 data points, the value is limited to position 1023, and no warning is sent. If the acquisition is a 4-Kbyte record and the position requested is past 4095 data points, a command error service request is generated, and the command is ignored. |
| **2221 and 2230**<br />CURSor SELect:{CURS1-CURS2} | Selects the named cursor to be positioned by the CURS POS command. |
| **2221 and 2230**<br />CURSor TARget:ACQuisition | Attaches the displayed cursors to acquisition waveform. |
| **2230**<br />CURSor TARget:{REF1-REF3} | Attaches the displayed cursors to the named reference waveform. If the named reference is not displayed, the command is ignored. No warning is issued for directing the cursors to an undisplayed reference. |
| **2221 and 2230**<br />CURSor TARget:REF4 | Attaches the displayed cursors to REF4. No warning is issued for directing the cursors to REF4 if it is not displayed, but an execution error service request is generated if REF4 is empty. |
| **2221 and 2230**<br />CURSor? | Returns all the present cursor argument states in the form: CURS SEL:CH1, TAR:ACQ,CHA:CH1,POS:1047;. Each of the CURSor arguments may be separately queried as in: CURSOR? TAR to obtain the present status of that argument only. |
| **2221 and 2230**<br />DELTAV? | Returns an {NR3} value that represents the present voltage difference between the selected TARget and CHAnnel cursors and the measurement units as either V or PERcent. The form of the return is: DELTAV VAL:0.500E0,UNI:VOL;. PERcent is returned for the units when the VOLT/DIV variable knob is out of the CAL detent position. |
| **2221 and 2230**<br />DELTAV? VALue | Returns the cursor voltage difference only in the form: DELTAV VAL:{NR3};. The return defaults to a displayed CHAnnel even if directed elsewhere to an undisplayed CHAnnel. |
| **2221 and 2230**<br />DELTAV? UNIts | Returns the voltage measurement units only in the form: DELTAV UNI:{V or PER};. See the preceding DELTAV? query description. |

<!-- page 7-30 -->

### Table 7-25: Cursor Commands (cont)

| Commands | Description |
|---|---|
| **2221 and 2230**<br />DELTAT? | Returns an {NR3} value that represents the present time difference between the two cursors with the measurement units in the form: DELTAT VAL:1.180E-3, UNI:SEC;. The measurement units are returned in DIVisions if the SEC/DIV setting is EXT CLK. |
| **2221 and 2230**<br />DELTAT? VALue | Returns the cursor time difference only in the form: DELTAT VAL:{NR3};. Time difference is returned even when the readout is in frequency units for 1/Δt measurements. |
| **2221 and 2230**<br />DELTAT? UNIts | Returns the time measurement units only in the form: DELTAT UNI:{S or DIV};. See the preceding DELTAT? query description. |

<!-- page 7-31 -->

### Table 7-26: Display Commands

| Commands | Description |
|---|---|
| **2221 and 2230**<br />MESsage {NR1}:"message" | Writes the "message" text on the named row. Values of {NR1} row numbers are from 16 (the top row) to 1 (the bottom row). The normal readout displays are turned off by the MESsage {16-1} command. Changing a front-panel control that requires a readout overrides the "message" and returns the normal readout display. The MES [0] command turns off the message display and also turns the normal readout displays back on (the zero may be omitted from the command). The message must be enclosed in quote marks. The displayed message lines start at the left edge of the graticule area. If longer than about 40 characters, the message runs off the right edge of the CRT. If the message is too long, it is truncated, and a service request is issued (if RQS is ON). Displaying many message lines can cause display flicker and may exceed the display memory area. |
| **ALL**<br />PLOt ABOrt | Stops a plot in progress and returns to the previous mode. PLOt ABOrt is the only command or query that the oscilloscope responds to during a plot. PLOt ABOrt turns off the AUTo argument. |
| **2221 and 2230**<br />PLOt AUTo:{ON or OFF} | Turns the AUTo plot mode ON or OFF. If AUTo is ON, each waveform is plotted after it is acquired. The graticule will be plotted once in AUTo, if GRAt is ON. |
| **ALL**<br />PLOt FORmat:{[XY], HPGl, EPS7, EPS8, or TJEt} | Sets the output data format for the named plotter. If one of the named plotters is not selected, the data is plotted in the default XY format. HPGl formats for HP-GP compatible plotters. EPS7 and EPS8 format for 7-bit (low-speed, double-density) and 8-bit (high-speed, double-density) EPSON(R) format printers respectively. TJEt formats for the Hewlett-Packard ThinkJet(R) printer. With Option 10, a GPIB controller may direct the plotting operation by addressing the plotter to listen and then addressing the oscilloscope to talk and giving the PLOt STArt command. |
| **ALL**<br />PLOt GRAt:{ON or OFF} | Turns the plotted graticule either ON or OFF. |
| **ALL**<br />PLOt SPEed:{NR1} | The {NR1} number must be an integer from 1 to 10 and changes the analog plotter pen speed. The units are roughly in divisions per second. |
| **ALL**<br />PLOt STArt | Starts a plot using the parameters selected by PLOt FORmat, PLOt GRAt, and PLOt SPEed. While a plot is in progress, all commands and queries (except PLOt ABOrt) are ignored. |

<!-- page 7-32 -->

### Table 7-27: Acquisition Commands

| Commands | Description |
|---|---|
| **ALL**<br />ACQuisition CURRent:{AVErage, [DEFault], PEAkdet, or SAMple} | Selects the named mode for the CURRent acquisition type and SEC/DIV setting. If a mode argument is not specified, the command selects the default mode for the present acquisition type and SEC/DIV setting. A service request is generated if the mode asked for is not valid with the present acquisition type or SEC/DIV setting. |
| **2221 and 2230**<br />ACQuisition CURRent:ACCpeak | Selects the ACCpeak mode for the current acquisition type and SEC/DIV setting. |
| **2221 and 2230**<br />ACQuisition HSRec:{ACCpeak or AVErage} | Selects the named mode for the SEC/DIV settings for 5 μs/div and 10 μs/div. |
| **ALL**<br />ACQuisition HSRec:[SAMple] | Selects the SAMple mode for the acquisitions made at 5 μs/div and 10 μs/div. This is the default mode and will be selected if the mode argument is omitted. |
| **2221 and 2230**<br />ACQuisition LSRec:{ACCpeak or AVErage} | Selects the ACCpeak or AVErage mode for acquisitions made at 0.02 ms/div to 50 ms/div. |
| **ALL**<br />ACQuisition LSRec:{[PEAkdet] or SAMple} | Selects the PEAkdet or SAMple mode for acquisitions made at 0.02 ms/div to 50 ms/div. PEAkdet will be selected if the argument to LSRec is omitted. |
| **2221 and 2230**<br />ACQuisition NUMsweeps:{NR3} | Sets the number of sweeps done before halting; 0 implies continuous mode (don't halt). |
| **2221 and 2230**<br />ACQuisition REPetitive:{ACCpeak or SAMple} | Selects the named mode for repetitive acquisitions at SEC/DIV settings from 0.05 μs/div to 2 μs/div. |
| **ALL**<br />ACQuisition REPetitive:[AVErage] | Selects the AVErage mode for repetitive acquisitions for SEC/DIV settings from 0.05 μs/div to 2 μs/div. This is the default argument and will be selected if the mode argument is omitted. |
| **ALL**<br />ACQuisition RESet | Command only. Sets sampling at all SEC/DIV settings to its default mode. Default modes are enclosed in brackets ([]) in the commands. |
| **ALL**<br />ACQuisition ROLl:{[PEAkdet] or SAMple} | Selects the PEAKdet or SAMple mode for ROLl acquisitions from 0.1 sec/div to 5 sec/div. ROLl mode acquisitions are untriggered. |
| **ALL**<br />ACQuisition SCAn:{[PEAkdet] or SAMple} | Selects the PEAkdet or SAMple mode for SCAn acquisitions. |

<!-- page 7-33 -->

### Table 7-27: Acquisition Commands (cont)

| Commands | Description |
|---|---|
| **2221 and 2230**<br />ACQuisition SCAn:{ACCpeak or AVErage} | Selects the ACCpeak or AVErage mode for SCAn acquisitions at 0.1 sec/div to 5 sec/div. The oscilloscope must in NORM or SGL SWP trigger mode to observe a change in the READOUT. |
| **ALL**<br />ACQuisition SMOoth:{ON or OFF} | Applies smoothing to the acquired waveform data when ON. |
| **2220**<br />ACQuisition TRIGCount: {NR1} | Sets the number of data points acquired before the trigger point in the waveform record. The range of the {NR1} number is 16 to 2048 in post-trigger and 2048 to 4080 in pre- or mid-trigger. The resolution of the {NR1} value is 4. |
| **2221**<br />ACQuisition TRIGCount: {NR1} | Sets the number of data points acquired before the trigger point in the waveform record. The range of the {NR1} number is from 16 to 4080. The setting of the front-panel TRIG POS switch does not limit the range of the trigger point position within the waveform record. The resolution of the {NR1} value is 4. |
| **2230**<br />ACQuisition TRIGCount:{NR1} | Sets the number of data points acquired before the trigger point in the waveform record. The range of the {NR1} number depends on the record length and the selection of pre- or post-trigger. In pretrigger, the {NR1} range is 4 to 512 for 1 K records and 16 to 2048 for 4 K records. In post-trigger, the range is from 512 to 1020 for 1 K records and 2048 to 4080 for 4 K records. The resolution of {NR1} is ±4 counts. |
| **ALL**<br />ACQuisition VECtors:{ON or OFF} | Turns point-to-point display vectors ON or OFF. |
| **ALL**<br />ACQuisition WEIght:{NR1} | Sets the number of acquisitions weighted into an AVEraged waveform record. The valid values for {NR1} are: 1, 2, 4, 8, 16, 32, 64, 128, and 256. A service request is generated and the command is ignored if the argument is not one of these numbers. If the argument for WEIght is omitted, {NR1} reverts to 4. |
| **ALL**<br />ACQuisition? | Returns the settings of the acquisition modes in the following short form with LONG set to OFF.<br /><br />`ACQ REP:AVE,HSR:SAM,LSR:PEA,SCA:PEA,ROL:PEA,SMO:ON,WEI:4,SWP:1037,NUM:0,POI:4096,TRIGM:POST,TRIGC:2000,SAV:OFF,DIS:SCA,VEC:ON;`<br /><br />Each of the acquisition command arguments (except RESet) may be queried separately to find out that argument's status. |
| **ALL**<br />ACQuisition? DISplay | Returns a string of either ROLl or SCAn for the present state of the ROLL/SCAN button. The form of the return is:<br /><br />`ACQ DIS:{ROL or SCA};` |

<!-- page 7-34 -->

### Table 7-27: Acquisition Commands (cont)

| Commands | Description |
|---|---|
| **ALL**<br />ACQuisition? POInts | Returns an {NR1} value that is the number of data points in the waveform record. The form of the return is:<br /><br />`ACQ POI:{NR1};` |
| **ALL**<br />ACQuisition? SAVE | Returns a string of either ON or OFF for the present state of the acquisition system (ON for SAVE and OFF for CONTINUE). |
| **ALL**<br />ACQuisition? SWPcount | Returns an {NR1} value for the number of sweeps completed in an acquisition. The form of the return is:<br /><br />`ACQ SWP:{NR1};` |
| **ALL**<br />ACQuisition? TRIGMode | Returns a string of either PRE or POST for the present ACQuisition Trigger setting in the following form:<br /><br />`ACQ TRIGM:{PRE or POST};` |
| **ALL**<br />STORe? | Returns the present state of the STORE/NON-STORE button in the form:<br /><br />`STOR {ON or OFF};` |

### Table 7-28: Save and Recall Reference Commands

| Commands | Description |
|---|---|
| **ALL**<br />REFFrom [ACQ] | Selects the acquisition as the source for the waveform data to be saved into one of the numbered reference memories by the SAVeref command. ACQ is the default argument (indicated by the square brackets, []) and need not be present in the command to select it as the data source. For the 2220 and 2221, ACQ is the only valid source. |
| **2230**<br />REFFrom REF{1-4} | Selects the named reference memory as the data source for the next SAVeref command. Acquisition (ACQ) waveforms must first be stored into one of the numbered references (REF1-REF4) before they may be saved into one of the lettered references (REFA-REFZ). |

<!-- page 7-35 -->

### Table 7-28: Save and Recall Reference Commands (cont)

| Commands | Description |
|---|---|
| **2230**<br />REFFrom REF{A-Z} | Selects the named extended memory location (REFA-REFZ) as the source of waveform data for the next SAVeref command. The total extra memory is 26 Kbytes, and stored waveform records of 1 K to 8 K (averaged 4 K acquisitions) may be stored. The nonvolatile references of the 2230 may not be displayed, plotted, or transmitted directly; they must first be moved to one of the numbered references (REF1-REF4) using the REFFrom and SAVeref commands. |
| **2230**<br />REFDisp REF{1-3}:ON, OFF or EMPTY} | Turns the named 2230 reference display ON or OFF. EMPTY erases the named 2230 reference display and turns it off. Reference memory locations 1, 2 and 3 are 1024-point memories. |
| **ALL**<br />REFDisp REF4: {ON, OFF or EMPTY} | REF4 is the only available reference memory for the 2220 and 2221 instruments. Reference memory 4 stores a 4 K (4096-point) reference waveform and occupies the REF1-REF3 memory locations in the 2230. |
| **2230**<br />REFDisp REF{A-Z}:EMPTY | The EMPTY command erases the named reference if it is not protected (see REFProt command). The lettered references may not be displayed directly; they must be moved to a numbered save reference memory (REF1-REF4). |
| **2230**<br />REFProt REF{A-Z}:{LOCked, PERM, or UNLocked} | These commands control the write protection of the 2230 nonvolatile reference memories (REFA-REFZ). LOCked and PERM disable further storage into the named reference or erasure of the waveform data. PERM protected waveform data cannot be overwritten using the front-panel controls. See REFStat queries to obtain write protection and bytes free status. |
| **2230**<br />REFOrmat CHAnnel:{[CH1] or CH2} | Selects which channel of the saved reference to REFOrmat. If there is no SAVE REF waveform for the named channel, a service request status byte is generated. If an XY waveform is selected for reformatting, either channel may be selected. CH1 is selected without the CH1 argument. |
| **2230**<br />REFOrmat HMAg:ON | Increases the horizontal gain of the REFORMAT TARget reference waveform set (affects vertical channels) by a factor of ten times. |
| **2230**<br />REFOrmat HMAg:OFF | Turns off the horizontal magnification of the REFORMAT TARget reference waveform set. |

<!-- page 7-36 -->

### Table 7-28: Save and Recall Reference Commands (cont)

| Commands | Description |
|---|---|
| **2230**<br />REFOrmat VGAin:{NR3} | Changes the vertical gain of the reference target and channel designated by REFOrmat TARget and REFOrmat CHAnnel. This command is not valid for XY waveforms. The maximum {NR3} value permitted is the equivalent of ±3 detent positions of the VOLT/DIV switch (in a 1-2-5 sequence). An execution error status byte is generated either if the asked-for setting is out of the maximum change range or if it is not a 1-2-5 sequence setting. |
| **2230**<br />REFOrmat VPOsition:{NR2} | Adjusts the vertical position of the reformatting target waveform. The valid range of {NR2} is ±10 divisions from the original display position (before any reformatting) with a resolution of one displayed bit. |
| **ALL**<br />REFDisp? | Returns the status of the REF1 reference memory location as ON, OFF, or EMPTY for the 2230; returns the status of REF4 for the 2220 and 2221. |
| **2230**<br />REFDisp? REF{1-3} | Returns the status of the named 2230 reference memory location as ON, OFF, or EMPTY. |
| **ALL**<br />REFDisp? REF4 | Returns the status of REF4 as ON, OFF, or EMPTY. For the 2210 and 2221 instruments, the default argument of REF4 is not needed. |
| **ALL**<br />REFFrom? | Query returns the selected source of waveform data for the SAVeref command. The reply will be ACQ for the 2220 and 2221; for the 2230 it may be from ACQ or any REFerence from (REF1-REF4) and (REFA-REFZ). |
| **2230**<br />REFOrmat? | Returns the status of the REFOrmat command and query arguments. A sample return is: `REFO TAR:REF4, CHA:CH2,VGA:0.5E+0, VPO:+3.96, HMA:OFF, BAS:0.2E+0,MOD:CH1;` Each of the command arguments may be individually queried for their status with respect to the REFOrmat TARget and CHAnnel reference waveform. |
| **2230**<br />REFOrmat? BASegain | Returns the vertical gain setting at which the REFOrmat TARget waveform was acquired as an {NR3} number. |
| **2230**<br />REFOrmat? MODe | Returns the vertical mode in which the REFOrmat TARget waveform was acquired (CH1, CH2, ADD, CHOP, ALT, or XY). |
| **2230**<br />REFStat? FILl | Returns a thirty-number string that indicates the fill status of each of the reference memories from REF1 to REFZ. The numbers are 0 (empty), 1, 2, 4, or 8 and indicate the stored waveform record in Kbytes. |

<!-- page 7-37 -->

### Table 7-28: Save and Recall Reference Commands (cont)

| Commands | Description |
|---|---|
| **2230**<br />REFStat? FREe | Returns the number of free Kbytes in the nonvolatile reference memory as a {NR1} number from 0 to 26. |
| **2230**<br />REFStat? PROTect | Returns a thirty-character string that indicates the protected status of each of the reference memories from REF1 to REFZ. The characters returned are U, L, or P and correspond to unlocked, locked, or permanent protection status. |
| **2230**<br />SAVeref REF{1-3} | Command only. Saves the waveform selected by the REFFrom command into the named reference. REF1, REF2, and REF3 are 1 K (1024-point) memory locations. Any 1 K portion of 4 K waveform acquisition (from ACQ or REF4) may be saved as a 1 K reference in REF1-REF3; the 1 K portion stored into REF1-REF3 is determined by the position of the active cursor. The saved reference display is also turned on. |
| **ALL**<br />SAVeref REF4 | Command only. REF4 is a 4 K (4096-point) memory location. It is the only reference memory for the 2220 and 2221 instruments, and as such the REF4 argument may be omitted from the SAV command for those instruments. |
| **2230**<br />SAVeref REF{A-Z} | Command only. Saves the waveform selected by the REFFrom command into the named reference (REFA-REFZ). Reference waveforms stored as 4 K records cannot be moved as 1 K records into REF1-REF3; to be either displayed or tranmitted 4 K records must be moved into REF4. |

**[Note: the manual's own printed Table 7-28 repeats the three rows above (SAVeref REF{1-3}, SAVeref REF4, SAVeref REF{A-Z}) a second time, verbatim, immediately following - transcribed here once since the duplicate row text is identical; this appears to be a printing artifact in the original manual, not additional content.]**

<!-- page 7-38 -->

### Table 7-29: Waveform Commands

| Commands | Description |
|---|---|
| **ALL**<br />CURVe | Use as a command to send waveform data to the oscilloscope. The DATa TARget command points to the reference memory where the data is sent. The DATa CHAnnel command points to the channel where the data is sent. (Only REF4 is available for the 2220 and 2221.) The DATa ENCdg command tells the oscilloscope the format of the data (HEX, BINary, or ASCii). Use as a query to get waveform data from the oscilloscope. The DATa SOUrce and DATa CHAnnel commands select the source of the waveform data. The data sent or received is in the form:<br /><br />`CURVE {data};` where the `{data}` is encoded for HEX, BINary, or ASCii in the following form:<br /><br />`%{byte count}{binary data}{checksum}` for BIN,<br />`#H{byte count}{hex data}{checksum}` for HEX, or<br />`{ascii data}` for ASCii encoding.<br /><br />With ASCii format, each data value is separated by a comma. |
| **ALL**<br />DATa CHAnnel:{[CH1] or CH2} | Selects the channel of a waveform set from which CURve?, WAVfrm?, or WFMpre? query will return data and the target channel for waveform data going into oscilloscope. If there is no waveform in the named channel, a service request is sent when the data is requested. At power-up, the selected channel is CH1. CH1 must be selected for an XY acquisition. |
| **ALL**<br />DATa ENCdg:{ASCii, [BINary], or HEX} | Sets the curve data encoding and decoding format. The power-on default is BINary. Data points are represented as unsigned integers in all formats. |

<!-- page 7-39 -->

### Table 7-29: Waveform Commands (cont)

| Commands | Description |
|---|---|
| **2230**<br />DATa SOUrce:{REF1, REF2, or REF3} | Selects the named reference memory to provide the waveform data for a WAV?, WFM?, or CURV? query. |
| **ALL**<br />DATa SOUrce:{[ACQ] or REF4} | Selects either the present acquisition or the REF4 reference memory to provide the waveform data for a WAV?, WFM?, or CURV? query. The power-on default is ACQ, and it will be selected if the argument is omitted. A saved 4 K record is moved from the instrument by specifying REF4 as the data source. |
| **2230**<br />DATa TARget:{REF1, REF2, or REF3} | Selects the named reference memory to receive data sent with a CURve or WFMpre command. At power-on, REF1 is selected. There is no default selection. |
| **ALL**<br />DATa TARget:REF4 | Selects REF4 as the reference memory to receive data sent with a CURve or WFMpre command. This is the only selection for the 2220 and 2221. For the 2230, REF4 must be selected as the data target to transfer in a 4 K waveform. |
| **ALL**<br />DATa? | Returns the selection of data source, target, channel and encoding. The short form of the return is:<br /><br />`DAT SOU:ACQ,TAR:REF1,CHA:CH1,ENC:BIN;`<br /><br />Each DATa argument may be individually queried to obtain that selection only. |
| **ALL**<br />WAVfrm? | Returns the waveform data from the oscilloscope. The return is the combined waveform preamble and waveform data. The waveform assigned by the DATa SOUrce and DATa CHAnnel commands is sent in the encoding assigned by the DATa ENCdg command. The form of the return is:<br /><br />`WFM {ascii preamble};CURV {waveform data};` |

<!-- page 7-40 -->

**NOTE**

*The information given in the Waveform Preamble Fields table is primarily to help identify the result of a WFMpre? query. As such, the arguments are not usually sent as individual commands, but are grouped together as a complete waveform preamble. If sent as a single command, an argument value is not accepted (except as noted for ENCdg) until the curve it is supposed to go with is transferred to the selected DATa TARget reference memory. If any size error in any of the waveform preamble numeric arguments is sent to the oscilloscope, it will be accepted. Then, when the curve data is sent the error will be rejected, and a waveform preamble error service request will be sent.*

### Table 7-30: Waveform Preamble Fields

| Commands | Description |
|---|---|
| **ALL**<br />WFMpre ENCdg:{ASCii, [BINary], or HEX} | Selects the waveform curve data encoding format for transferring data. WFEpre ENCdg and DATa ENCdg operate identically. Data points are represented as unsigned integers in any of the encoding formats. |
| **ALL**<br />WFMpre? | Returns the waveform identification string as with the WFMpre? WFId query plus the value for all the waveform preamble arguments. The short form of the return is:<br /><br />`WFM WFI:"{identification string}", NR.P:2048, PT.O:256, PT.F:ENV, XMU:1.0E+3, XOF:0, XUN:S, XIN:10.0E-6, YMU:8.0E-3, YOF:0, YUN:V, ENC:ASC, BN.F:RP, BYT:1,BIT:8,CRV:CHK;`<br /><br />Each of the arguments may be queried separately to find out its value. |
| **ALL**<br />WFMpre? WFId | Returns an ASCII waveform identification string giving the key features of the waveform. The information returned is: acquisition source, channel, Volts/Div, input coupling, Sec/Div, acquisition mode, and the number of the curve being sent. In XY mode, the CH2 Volts/Div and input coupling are added. The waveform ID is ignored if received as a command. The form of the return is:<br /><br />`WFM WFI:"ACQ, CH1, 0.2mV, DC, 0.5mS, AVERAGE, CRV# 3";`<br /><br />or for XY:<br /><br />`WFM WFI:"REF4, XY, 20mV, DC, 50mV, DC, 0.5mS, SAMPLE, CRV# 1";`<br /><br />**NOTE**<br /><br />*The DATa CHAnnel must be CH1 to get the XY information. All vertical information is omitted for a 2220.* |

<!-- page 7-41 -->

### Table 7-30 (cont)

| Commands | Description |
|---|---|
| **ALL**<br />WFMpre NR.Pts:{NR1} | {NR1} is the number of points in the waveform. Each point can be a single Y value (with time implied), an X-Y pair, or a Max-Min pair. Although the record length is either 1024 data point or 4096 data points, the NR.Pts {NR1} value may be 256, 512, 1024, 2048, or 4096 points. The value depends on the number of channels, the acquisition mode, and whether smoothing is on or off. A table expressing the conditions and the record length to NR.Pts ratio value follows:<br /><br />**NR.Pts to Record Length Ratio table**<br /><br />| NR.Pts to Record Length Ratio | Number of Channels | Acquire Mode | SMOOTH |<br />|---|---|---|---|<br />| Rec/1 | 1 | SAMple | NA |<br />| Rec/1 | 1 | AVErage | NA |<br />| Rec/1 | 1 | PEAkdet | ON |<br />| Rec/1 | 1 | ACCpeak | ON |<br />| Rec/2 | 2 | SAMple | NA |<br />| Rec/2 | 2 | AVErage | NA |<br />| Rec/2 | 2 | PEAkdet | ON |<br />| Rec/2 | 2 | ACCpeak | ON |<br />| Rec/2 | 1 | PEAkdet | OFF |<br />| Rec/2 | 1 | ACCpeak | OFF |<br />| Rec/4 | 2 | PEAkdet | OFF |<br />| Rec/4 | 2 | ACCpeak | OFF |<br /><br />For example, if the number of channels is two and the acquisition is peak detect with smoothing off, the number of points for a waveform in a 4 Kbyte record is 4096 divided by 4 (1024 points). |
| **ALL**<br />WFMpre PT.Off:{NR1} | {NR1} is the trigger position relative to the first data point in the record. For a 1024 point record, {NR1} for PT.Off ranges from 4 to 1024 in increments of 4. The normal values for a 4096 point record range from 4 to4096.<br /><br />**NOTE**<br /><br />*{NR1} will be a negative value if the trigger occurred before the first data point in the record window. Since any 1024 point window of a 4096 point record may be transferred, the legal values of {NR1} for PT.Off are -3096 to +4096. If the PT.Off value is unknown, -10000 is the {NR1} value returned.* |

<!-- page 7-42 -->

### Table 7-30 (cont)

| Commands | Description |
|---|---|
| **ALL**<br />WFMpre PT.Fmt:{Y, XY, or ENV} | Point format defines how to interpret the curve data points.<br /><br />Y format means that X-axis information is derived from the waveform preamble and not sent explicitly. The data values represent the vertical amplitude of the waveform at that data point position.<br /><br />XY format means that the data points are in X-Y pairs, with X first.<br /><br />ENV format means that the vertical data is sent in max-min pairs. The data is sent in the form:<br /><br />`....,y1max,y1min,y2max,y2min,..`<br /><br />However, the max-min data is displayed in the reverse order, with min data first then max data (...,y1min,y1max,y2min,y2max,...).<br /><br />ENV is valid for PEAkdet and ACCpeak acquisition modes with SMOoth OFF. |
| **ALL**<br />WFMpre XUNits:{S or CLKs} | Gives the units value for the XINcr. If XUN is S the X-increment is in seconds; if in CLK, the X-increment is unknown. (CLK is returned when the SEC/DIV setting is EXT CLK.) |
| **ALL**<br />WFMpre XINcr:{NR3} | The XINcr {NR3} value is the time between data points. If XINcr for a waveform being sent to the oscilloscope does not correspond to a legitimate SEC/DIV setting, the new curve data is not accepted, and a command argument error service request is sent (if RQS is ON). The queried XINcr value of {NR3} is set equal to 1 (0.1E+0) if it is unknown, as is the case for EXT CLK. |
| **ALL**<br />WFMpre YUNits:{V or DIVs} | Indicates the units of YMUlt. When the CAL knob of the DATa CHAnnel is not in the detent position, the DIVs argument is returned. DIVs is always returned for the 2220 since the vertical scaling is unknown. |
| **ALL**<br />WFMpre YMUlt:{NR3} | The YMUlt {NR3} value is the step size of the digitizer (volts between digitizer levels). I the YMUlt for a waveform being sent to the oscilloscope does not correspond to a legitimate VOLTS/DIV setting, the new curve data is not accepted, and waveform preamble error service request is sent (if RQS is ON). The queried YMUlt value of {NR3} is 40.0E-3 when the VOLTS/DIV CAL knob for the DATa SOUrce is not in the detent position. |
| **ALL**<br />WFMpre YOFf:{NR1} | The YOFf {NR1} value is the Y coordinate of ground. If ground level is not known, the value of -10000 is returned. |
| **ALL**<br />WFMpre XMUlt | XMUlt and XOFf are similar to YMUlt and WFMpre XOFf YOFf. They are added to the waveform preamble for XY waveforms. For all XY waveforms, the YUNits value is valid for both the X and the Y data points. The value of XUNits is referenced to the sampling rate. |

<!-- page 7-43 -->

### Table 7-30 (cont)

| Commands | Description |
|---|---|
| **ALL**<br />WFMpre BN.Fmt:RP | RP is the only argument valid argument. It means that the binary format is always right-justified and consists of positive binary integers (also known as unsigned binary integers). |
| **ALL**<br />WFMpre BYT/nr:{NR1} | The valid numbers for {NR1} are 1 and 2. Each data point value is represented by two bytes for AVErage mode, only one byte in other modes. If two bytes are sent, the most significant byte is sent first.<br /><br />In HEX format, each data point is represented by two ASCII encoded hex characters. |
| **ALL**<br />WFMpre BIT/nr:{NR1} | The data points consist of either 8 or 16 bits.<br /><br />**NOTE**<br /><br />*The least significant bits of a 16-bit waveform may or may not be valid, depending on the number of acquisitions averaged.* |
| **ALL**<br />WFMpre CRVchk:CHKsm0 | The CHKsm0 argument indicates that the last byte of a binary curve is a checksum. The checksum byte is the two's complement of the modulo 265 sum of the binary count and curve data bytes. It does not include the word and symbol CURVE % that comes before the binary count. |

<!-- page 7-44 -->

### Table 7-31: Miscellaneous Commands

| Commands | Description |
|---|---|
| **ALL**<br />INIt | Command only. The INIt command causes the oscilloscope revert to the power-on default states for the acquisition modes. The 2230 menu system is also initialized. |
| **ALL**<br />LONg {[ON] or OFF} | With LONg ON, replies to queries are reported with the full command words. With LONg OFF, replies use the short form of the command words. The short form characters are those that appear in capitol letters in these command tables and are the minimum characters accepted as valid for commands. The power-on and default states of LONg are ON. The LONg? query returns its state, ON or OFF. |
| **ALL**<br />ID? | Returns the oscilloscope identification string in the form:<br /><br />`ID TEK/2230,V81.1,VERS:09;`<br /><br />The instrument type and version numbers will be reported as appropriate for the instrument queried. |
| **ALL**<br />HELp? | Returns a list of all the valid command headers available in the instrument queried. All the valid characters of the commands are returned; the short form of the commands (LONG OFF) are in capital letters. |
| **ALL**<br />SET? | Returns an ASCII string of headers and arguments reflecting the present states of the controls and modes that may be set via the communications interface. The query-only settings are not returned. The string returned by the SET? query may be sent as a command message to the oscilloscope to recreate those settings. The state of the LONg command affects the length of the reply.<br /><br />**NOTE**<br /><br />*To comply to Codes and Formats, a header is not sent back with the settings string.* |

<!-- page 7-45 -->

### Table 7-32: Service Request Group Commands

| Commands | Description |
|---|---|
| **ALL**<br />OPC {[ON] or OFF} | When ON, the oscilloscope sends a service request upon completion of certain system events (if RQS is also ON). Events that request service when completed with OPC ON include: Acquisition completed, and plot completed. When off, OPC (operation completed) events do not generate a service request. The power-on state of OPC is OFF. |
| **ALL**<br />RQS {[ON] or OFF} | When ON, the oscilloscope sends a service request (SRQ) when it has an event to report. When OFF, event codes of different priority still accumulate and may be retrieved with an EVEnt? query, but the reply to STAtus? will be a 0. The power-on and default states of RQS are ON. |
| **ALL**<br />EVEnt? | Returns an {NR1} value that is the code number for oldest service-request event (if multiple events are pending). If no events are pending, {NR1} is 0. Multiple events of different priority are retrieved by sending EVEnt? until 0 is returned. Querying the event clears the service request. |

<!-- page 7-46 -->

### Table 7-33: RS-232-C Specific Commands

| Commands | Description |
|---|---|
| **ALL**<br />FLOw {[ON] or OFF} | Enables (ON) or disables (OFF) DC1/DC3 flow control. FLOW ON is the default and power-on state. Binary data transfers cannot be made with FLOW ON. A FLOw? query returns the present state, ON or OFF.<br /><br />With FLOW ON, the {control-S}, {control-Q}, and {control-D} are recognized during data transfers. Their functions are as follows:<br /><br />&lt;control-S&gt; — Temporarily suspend output of characters.<br /><br />&lt;control-Q&gt; — Resume character output that has been temporarily suspended.<br /><br />&lt;control-D&gt; — Abort the command or query execution; erase both input and output buffers; reset the message processor. |
| **ALL**<br />REMote {[ON] or OFF} | Enables (ON) or disables (OFF) setting of remote-controllable oscilloscope states. An execution error service request is sent if a control command is sent with REM OFF.<br /><br />REM? returns the present state, ON or OFF. |
| **ALL**<br />STOP {1 or 2} | Sets the number of stop bits used in transferring character codes. The usual selection is 1 though some printers require two stop bits at certain baud rate settings. STOP is set to 1 at power on. When connecting to a printer or plotter, select a baud rate that uses only one stop bit.<br /><br />STOP? returns the present setting, 1 or 2. |
| **ALL**<br />STAtus? | Returns the current status of the instrument. If no service requests are pending, the status byte returned indicates No Status to Report. If RQS is off, an EVEnt? query must be used to find out if an event occurred and, if so, which one. The EVEnt? query produces more useful information about an event than the service request status byte. |

<!-- page 7-47 -->

## STATUS BYTES AND EVENT CODES

The various status events and errors that can occur are divided into several categories as defined in Table 7-34. Table 7-35 lists the event codes that are returned as the result of an EVEnt? query.

### Option 10

If there is more than one event of different priority levels to be reported, the oscilloscope reasserts SRQ until it reports all events of different priority. It does not issue an SRQ for duplicate events pending or for more than one event of the same priority level. Each event is automatically cleared when its status byte is reported. The controller option can clear all events by repeatedly sending the EVEnt? query until a zero status byte is returned. The Device Clear (DCL) interface message may be used to clear all events, except the power-on event.

With RQS set OFF, all service requests (except the power-on SRQ) are prevented. With the service requests turned off, the EVEnt? query must be sent to the oscilloscope so that the controller can determine the oscilloscope and event status. The controller may address the oscilloscope and send the STAtus? or EVEnt? query at any time. It is not necessary to wait for an SRQ. The instrument will return the status byte code for STA? status bytes pending and an event code for EVE? for events waiting to be reported (or a 0 for no events to report).

### Option 12

If there is more than one event of different priority levels to be reported, the oscilloscope has a status byte and event code available for each one. It does not report duplicate events or more than one event of the same priority level. Each event is automatically cleared when its status byte or event code is reported. The Device Clear (DCL) interface message may be used to clear all events, except the power-on event. Querying EVEnt? until the return is EVE 0 clears all pending status bytes and there is no power-on event.

With RQS set OFF, all service requests are prevented. With the service requests turned off, the EVEnt? query must be sent to the oscilloscope so that the controller can determine the oscilloscope and event status. The controller may send the EVEnt? query at any time, and the instrument will return the code for an event waiting to be reported (or a 0 for no events to report). The controller can clear all events by repeatedly sending the EVEnt? query until a zero status byte is returned.

### Table 7-34: Status Event and Error Categories

| Category | Binary(a) | Decimal RQS Off - Not Busy | Decimal RQS Off - Busy | Decimal RQS On - Not Busy | Decimal RQS On - Busy | Description |
|---|---|---|---|---|---|---|
| Command Error | 0R1X 0001 | 33 | 49 | 97 | 113 | The instrument received a command that it cannot understand. |
| Execution Error | 0R1X 0010 | 34 | 50 | 98 | 114 | The instrument received a command that it cannot execute. This is caused by either out-of-range arguments or settings that conflict. |
| Internal Error | 0R1X 0011 | 35 | 51 | 99 | 115 | The instrument detected a hardware condition or a firmware problem that prevents operation. |
| Power.On | 010X 0001 | 1 | 17 | 65 | 81 | Instrument power was turned on. |
| Operation Complete | 0R0X 0010 | 2 | 18 | 66 | 82 | Operation complete. |
| Execution Warning | 0R1X 0101 | 37 | 53 | 101 | 117 | The instrument received a command and is executing it, but a potential problem may exist. For example, the instrument is out of range, but sending a reading anyway. |
| No Status | 000X 0000 | 0 | 16 | 0 | 16 | There is no status to report. |

(a) R is set to 1 if RQS is ON; otherwise it is 0. X is the busy bit and is set if the oscilloscope is busy at the time the status byte is read. Anytime the instrument is actively processing a command or query, the bit is a 1, otherwise it is a 0.

<!-- page 7-48 -->

cally cleared when its status byte is reported. The controller option can clear all events by repeatedly sending the EVEnt? query until a zero status byte is returned. The Device Clear (DCL) interface message may be used to clear all events, except the power-on event.

With RQS set OFF, all service requests (except the power-on SRQ) are prevented. With the service requests turned off, the EVEnt? query must be sent to the oscilloscope so that the controller can determine the oscilloscope and event status. The controller may address the oscilloscope and send the STAtus? or EVEnt? query at any time. It is not necessary to wait for an SRQ. The instrument will return the status byte code for STA? status bytes pending and an event code for EVE? for events waiting to be reported (or a 0 for no events to report).

event code available for each one. It does not report duplicate events or more than one event of the same priority level. Each event is automatically cleared when its status byte or event code is reported. The Device Clear (DCL) interface message may be used to clear all events, except the power-on event. Querying EVEnt? until the return is EVE 0 clears all pending status bytes and there is no power-on event.

With RQS set OFF, all service requests are prevented. With the service requests turned off, the EVEnt? query must be sent to the oscilloscope so that the controller can determine the oscilloscope and event status. The controller may send the EVEnt? query at any time, and the instrument will return the code for an event waiting to be reported (or a 0 for no events to report). The controller can clear all events by repeatedly sending the EVEnt? query until a zero status byte is returned.

### Table 7-35: Event Codes

| EVENT? Code | Instrument Status |
|---|---|
| 000 | No status to report |

**Command Errors**

| EVENT? Code | Instrument Status |
|---|---|
| 101 | Command header error. |
| 102 | Header delimiter error. |
| 103 | Command argument error. |
| 104 | Argument delimiter error. |
| 105 | Non-numeric argument, numeric expected. |
| 105 | Non-numeric argument, numeric expected. |
| 106 | Missing argument. |
| 107 | Invalid message-unit delimiter. |
| 108 | Checksum error. |
| 109 | Byte-count error. |

*(Note: EVENT? codes 105 appears twice in the original table, apparently a duplicate in the source printing.)*

<!-- page 7-49 -->

### Table 7-35 (cont)

| EVENT? Code | Instrument Status |
|---|---|
| 151 | The argument is too large. |
| 152 | Illegal hex character. |
| 153 | Non-binary argument; binary or hex expected. |
| 154 | Invalid numeric input. |
| 155 | Unrecognized argument type. |

**Execution Errors**

| EVENT? Code | Instrument Status |
|---|---|
| 201 | Command cannot be executed when in LOCAL. |
| 203 | I/O buffers full, output dumped. |
| 205 | Argument out of range, command ignored. |
| 206 | Group execute trigger ignored. |
| 251 | Illegal command. |
| 252 | Integer overflow. |
| 253 | Input buffer overflow. |
| 254 | Invalid waveform preamble |
| 255 | Invalid instrument state. |
| 256 | GPIB (Option 10) command not allowed. |
| 257 | RS-232-C (Option 12) command not allowed. |
| 258 | Command not allowed on 2220 or 2221. |
| 259 | Command not allowed on 2230. |
| 260 | Cannot execute command with RQS OFF. |
| 261 | Reference memory busy with local (front-panel) command. |
| 262 | Reference memory non-existent or specified as different size than selected waveform. |
| 263 | Plot active; only PLOT ABORT allowed while plotting. |

**Internal Errors**

| EVENT? Code | Instrument Status |
|---|---|
| 351 | Firmware failure. Contact your nearest Tektronix Service Center for assistance. |

<!-- page 7-50 -->

### Table 7-35 (cont)

**System Events**

| EVENT? Code | Instrument Status |
|---|---|
| 401 | Power on. |
| 451 | Parity error. |
| 452 | Framing error. |
| 453 | Carrier lost. |
| 454 | End of acquisition OPC. |
| 455 | End of plot OPC. |
| 456 | Diagnostics test complete OPC. |

**Execution Warnings**

| EVENT? Code | Instrument Status |
|---|---|
| 551 | Single sweep is already armed. |
| 552 | No ground-dot measurement available. |
| 553 | Invalid probe code or identify. |
| 554 | Query not valid for current instrument state. |
| 555 | Requested setting is out of detent (uncalibrated). |
| 556 | MESsage display buffer is full. |
| 557 | Waveform preamble is incorrect, has been corrected. |
| 558 | Waveform transfer ended abnormally. |

*(End of manual - page 7-50 is the last page.)*
