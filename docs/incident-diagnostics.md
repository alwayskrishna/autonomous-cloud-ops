# AI-ready incident diagnostics (PR #10)

The diagnostics layer introduces `IIncidentDiagnosticProvider` and `IncidentDiagnosticService`. The first provider, `MockIncidentDiagnosticProvider`, runs **entirely offline** with deterministic rule-based explanations for CPU, memory and HTTP 5xx incidents. It is not a language model and does not claim AI-generated findings.

```csharp
var service = new IncidentDiagnosticService(new MockIncidentDiagnosticProvider());
var assessment = await service.DiagnoseAsync(incident);
```

Assessments contain a summary, possible causes, investigation steps, confidence (0–1), and provider identifier. Mock assessments deliberately report **confidence 0**, since no real evidence-based model evaluation has occurred.

A future Azure OpenAI adapter can implement `IIncidentDiagnosticProvider` without changing the orchestration layer. It will require explicit opt-in, authentication, redaction, timeouts, bounded prompts, model output validation and evaluation before production use.

**Safety:** Diagnostic guidance is advisory only. No remediation, network access, Azure credentials, paid API calls, or cloud resources are used. Never treat hypotheses as confirmed root causes.
