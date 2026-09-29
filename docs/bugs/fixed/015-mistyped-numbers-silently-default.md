# 015: A mistyped baud rate or data bits silently connects with defaults

| | |
|---|---|
| **Severity** | Medium |
| **Status** | Fixed |
| **Confidence** | Confirmed |
| **Area** | DevTerm.Configuration (ConnectionEditorViewModel, CliOptionsValidator) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

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

## Resolution
Fixed in `dev/fix-bugs` on 2026-09-26: `ConnectionEditorViewModel.ValidateFields` now fails when Baud, DataBits,
VendorId or ProductId text doesn't parse as an integer, instead of silently keeping the `CliOptions()` default;
`CliOptionsValidator.Validate` gained range checks for Baud (> 0), DataBits (5-8), ReadTimeoutMs/WriteTimeoutMs
(>= -1) and PlaybackSpeed (>= 0), which also protects a profile loaded directly from JSON (bypassing the editor's
text fields entirely). Regression tests: `ConnectionEditorViewModelTests.SaveCommand_WithAnUnparseableBaud_SetsStatusMessageInsteadOfSavingWithTheDefault`,
`CliOptionsValidatorTests.Validate_SerialWithNonPositiveBaud_Fails`,
`Validate_SerialWithDataBitsOutOfRange_Fails`, `Validate_ReadTimeoutBelowNegativeOne_Fails`,
`Validate_WriteTimeoutBelowNegativeOne_Fails`, `Validate_NegativePlaybackSpeed_Fails`.
