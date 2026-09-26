# 049: UI definition XML is parsed without prohibiting DTDs

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
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
