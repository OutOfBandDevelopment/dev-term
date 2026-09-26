# 015: A mistyped baud rate or data bits silently connects with defaults

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Open |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionEditorViewModel, CliOptionsValidator) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
- `src/DevTerm.Configuration/ConnectionEditorViewModel.cs:1094-1102` (Baud, DataBits), `:1119-1127` (VendorId, ProductId)
- `CliOptionsValidator` has no ranges for Baud, DataBits, the timeouts or PlaybackSpeed.

## Failure scenario
- Baud `115200x`: `int.TryParse` fails, `options.Baud` keeps `new CliOptions()`'s 9600, and validation passes. The
  editor connects (and can save the profile) at 9600 with no error.
- `DataBits: 9`, `Baud: 0` or `ReadTimeoutMs: -5` in a profile pass validation and only fail when `SerialPort` opens,
  with an `ArgumentOutOfRangeException`.

## Suggested fix
Make an unparseable field a validation failure in `BuildOptions`/`ValidateFields`. Add validator ranges: Baud > 0,
DataBits 5-8, timeouts >= -1, PlaybackSpeed >= 0.

## Tests to add
`BuildOptions` with invalid Baud/DataBits text fails validation; validator range cases.
