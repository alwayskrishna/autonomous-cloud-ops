# JWT bearer authentication (PR #21)

Non-test API environments now require `Authentication:Authority` (HTTPS OIDC issuer) and `Authentication:Audience` (API audience). Missing configuration fails application startup. All endpoints except `/health` require a validated bearer token via ASP.NET Core's JWT bearer middleware and issuer metadata.

Example environment variables: `Authentication__Authority=https://login.microsoftonline.com/<tenant-id>/v2.0` and `Authentication__Audience=<api-app-id>`. Replace placeholders with your own identity tenant/app registration. This milestone **does not provision an Entra application registration**.

**Local test caveat:** the `Testing` environment deliberately bypasses authentication to preserve existing WebApplicationFactory tests. Never deploy using `ASPNETCORE_ENVIRONMENT=Testing`. Authentication is not tenant-level authorization; role/tenant enforcement is separate work. Do not expose this API publicly until all authorization and persistence gaps are addressed.
