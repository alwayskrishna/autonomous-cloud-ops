# Recovery action contracts and dry-run mode (PR #13)

The recovery layer introduces `RecoveryPlanner`, `RecoveryAction`, `RecoveryPlan`, and `RecoveryDryRunService`. Plans are **advisory** and always require human approval before any future execution.

| Incident signal | Suggested action | Dry-run prediction |
| --- | --- | --- |
| CPU | ScaleOut (2 instances) | Hypothetical capacity increase |
| Memory | RestartService | Hypothetical service restart |
| Other | InvestigateOnly | Gather more evidence |

```csharp
var plan = new RecoveryPlanner().Propose(incident);
var simulation = new RecoveryDryRunService().Simulate(plan.Actions[0]);
Console.WriteLine(simulation.Executed); // false
```

The dry-run service validates IDs, rationale, action kinds and scale-out targets (2–10 instances). It returns predicted effects and safety checks but contains **no execution method**, infrastructure client or cloud SDK. It never restarts, scales or modifies resources.

These proposals are based on simple alert keywords, **not confirmed root causes**. In particular, restarting a memory-constrained service can be disruptive and must not be interpreted as a recommended production action without further evidence.

The next milestone adds human approval state and auditability; actual execution remains out of scope.
