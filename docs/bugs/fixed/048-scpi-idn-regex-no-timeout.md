# 048: SCPI *IDN? matching runs profile regexes with no timeout

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Confirmed (local files only) |
| **Area** | DevTerm.Devices.Scpi (ScpiProfileCatalog) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`src/DevTerm.Devices.Scpi/ScpiProfileCatalog.cs:61`

## What happens
`Regex.IsMatch` runs a profile-supplied pattern with no timeout; the manifest reply presenter and editor use 250 ms. A
catastrophic-backtracking pattern in a user profile would hang auto-detect.

## Suggested fix
Construct the regexes with a timeout (250 ms, as elsewhere).

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: `ScpiProfileCatalog.TryMatchByIdn`
(`src/DevTerm.Devices.Scpi/ScpiProfileCatalog.cs`) now calls `Regex.IsMatch` with a 250 ms timeout
(`RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)`), matching
`ManifestReplyPresenter`/`ManifestEditorForms`'s existing pattern, and catches
`RegexMatchTimeoutException` per profile — a profile whose pattern times out is treated as "doesn't
match" and the loop continues to the next profile, mirroring
`ManifestReplyPresenter.AddLineValues`'s own `catch (RegexMatchTimeoutException) { continue; }`. Split
into a public `TryMatchByIdn(string)` (against `All`) and an internal
`TryMatchByIdn(string, IEnumerable<ScpiInstrumentProfile>)` overload so a test can exercise matching
against a synthetic catastrophic-backtracking profile without depending on the process's real loaded
profiles.

Regression test:
`ScpiProfileCatalogTests.TryMatchByIdn_CatastrophicBacktrackingPattern_CompletesWithinTheRegexTimeoutBudget`
(an MSTest `[Timeout(1000)]` test — fails as "timed out" against the pre-fix code, confirmed via a
temporary revert of just the timeout/catch behavior). Full `TestCategory=Unit` run green across the
whole solution (no regressions).
