# Local container deployment and safe infrastructure templates (PR #18)

This repository supports a **localhost-only development container**. The REST API is not authenticated and must not be exposed to the public internet.

## Local Docker Compose

From the repository root:

```bash
docker compose up --build -d
curl http://127.0.0.1:8080/health
docker compose down
```

The host port binds to `127.0.0.1` only. A named Docker volume persists the SQLite incident database across container restarts. The runtime container uses a non-root user.

**Do not** change the host binding to `0.0.0.0`, publish the container to a public registry for deployment, or expose it through an ingress until authenticated tenant-scoped API authorization is implemented.

## Azure Bicep (template only)

`infra/main.bicep` contains a deliberately non-provisioning resource-group template. It produces metadata outputs and **zero Azure resources**. No Azure login, subscription or deployment is needed to review the source.

Optional syntax validation if Bicep CLI is installed:

```bash
az bicep build --file infra/main.bicep
```

The template is **not** a working cloud deployment and does not deploy Container Apps, AKS, networking, databases, or identity infrastructure. Cloud deployment requires a future security review, a real authentication provider, network restrictions, tenant isolation end-to-end, resource sizing, and explicit user approval for paid provisioning.

## Local validation

```bash
dotnet test tests/AutonomousCloudOps.Tests/AutonomousCloudOps.Tests.csproj -c Release
dotnet test tests/AutonomousCloudOps.Api.Tests/AutonomousCloudOps.Api.Tests.csproj -c Release
docker compose config
docker compose build
```

No GitHub Actions workflow in this PR logs into Azure or provisions resources.
