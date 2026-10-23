# Azure OpenAI evidence-aware diagnostics (PR #27)

`AzureOpenAiDiagnosticProvider` is an opt-in implementation of `IIncidentDiagnosticProvider`. It retrieves bounded evidence from `IncidentEvidenceService`, sends a restricted incident/evidence summary to an HTTPS Azure OpenAI chat completions endpoint, and parses structured advisory output. The caller supplies `IAzureOpenAiTokenProvider` and `HttpClient`; no credentials are stored in source. It is **not registered in the default API**, which continues to use the offline mock.

**Privacy:** incident reason and evidence summaries are sent to the configured Azure endpoint when invoked. Do not include customer content, credentials or secrets; the provider currently truncates input but does **not** implement robust PII/secret redaction. Review organizational data-handling requirements before use.

**Safety:** model content is untrusted and cannot invoke recovery tools. Output confidence is set to zero because it is not calibrated. Model response formatting and endpoint compatibility depend on deployment capabilities. A production version needs policy checks, redaction, token acquisition via managed identity, timeout/retry/circuit-breaker handling, grounding evaluation and adversarial prompt-injection tests. No Azure calls occur unless explicitly configured and invoked.
