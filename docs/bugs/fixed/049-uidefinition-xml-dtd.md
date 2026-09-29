# 049: UI definition XML is parsed without prohibiting DTDs

| | |
|---|---|
| **Severity** | Low |
| **Status** | Fixed |
| **Confidence** | Plausible |
| **Area** | DevTerm.UiDefinitions (UiDefinitionSerializer) |
| **Created** | 2026-09-26 |
| **Found at commit** | `42758db2e9fecc95584fe15465fd7f41e637e2e4` (`main`) |
| **Found by** | Static code review (read-only; not yet reproduced) |

## Where
`UiDefinitionSerializer.FromXml`

## What happens
`XmlSerializer.Deserialize(TextReader)` has no resolver, so external entities aren't fetched, but internal-entity
expansion (billion laughs) may still apply through a manifest's `UiFile` `.xml`.

## Suggested fix
Deserialize from `XmlReader.Create(reader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit })`.

## Resolution
Fixed on 2026-09-29 on `dev/fix-bugs`: reproduced first — a DOCTYPE with an internal entity
(`<!DOCTYPE UiDefinition [<!ENTITY evil "expanded">]>`) deserialized without error via
`UiDefinitionSerializer.FromXml`, confirming `XmlSerializer.Deserialize(TextReader)` applies no DTD
restriction on its own. Both `UiDefinitionSerializer.FromXml`
(`src/DevTerm.UiDefinitions/UiDefinitionSerializer.cs`) and `DeviceManifestSerializer.FromXml`
(`src/DevTerm.DeviceManifests/DeviceManifestSerializer.cs`) — which mirrors the same
`XmlSerializer.Deserialize(TextReader)` pattern one class down and is reachable from the same
untrusted `device.xml`/`UiFile` manifest input — now deserialize via
`XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit })` as
suggested, which throws (wrapped by `XmlSerializer` as `InvalidOperationException`) the moment a
DOCTYPE is encountered, before any entity can be defined or expanded.

Regression tests:
`UiDefinitionSerializerTests.FromXml_DocumentWithAnInternalDtdEntity_DoesNotExpandIt` and
`DeviceManifestTests.FromXml_DocumentWithAnInternalDtdEntity_DoesNotExpandIt` (both fail against the
pre-fix code — no exception was thrown and the entity reference was accepted). Full
`TestCategory=Unit` run green across the whole solution (no regressions).
