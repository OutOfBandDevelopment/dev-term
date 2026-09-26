# 049: UI definition XML is parsed without prohibiting DTDs

| | |
|---|---|
| **Severity** | Low |
| **Status** | Open |
| **Confidence** | Plausible |
| **Area** | DevTerm.UiDefinitions (UiDefinitionSerializer) |
| **Found** | 2026-09-26 code review of `main` @ `42758db` (branch `dev/review-code`); static review, not yet reproduced |

## Where
`UiDefinitionSerializer.FromXml`

## What happens
`XmlSerializer.Deserialize(TextReader)` has no resolver, so external entities aren't fetched, but internal-entity
expansion (billion laughs) may still apply through a manifest's `UiFile` `.xml`.

## Suggested fix
Deserialize from `XmlReader.Create(reader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit })`.
