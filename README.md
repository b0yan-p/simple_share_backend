# SimpleShare Backend

A .NET 9.0 Web API backend for the SimpleShare application.

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- SQL Server (localhost:1433)

## Getting Started

### 1. Clone the repository

```bash
git clone <repository-url>
cd simpleShare_backend
```

### 2. Configure the database

Update the connection string in `appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost,1433;Database=simple_share;User Id=sa;Password=<your-password>;Encrypt=False;Trusted_Connection=False;MultipleActiveResultSets=true;"
}
```

### 3. Configure JWT settings

Update the JWT configuration in `appsettings.json`:

```json
"JWT": {
  "Issuer": "http://localhost:5169",
  "Audience": "http://localhost:5169",
  "SigningKey": "<your-signing-key>"
}
```

### 4. Apply database migrations

```bash
dotnet ef database update
```

### 5. Run the application

```bash
dotnet run
```

The API will be available at `https://localhost:5169` (or the configured port).

## API Documentation

Swagger UI is available in development mode at `/swagger`.

## Container image & releases

`.github/workflows/publish.yml` builds the Docker image and pushes it to GHCR.

| Trigger | Image tags produced |
|---|---|
| push to `main` | `:latest`, `:sha-<short>` |
| push tag `v1.2.3` | `:1.2.3`, `:1.2`, `:sha-<short>` + a GitHub Release |

Image: `ghcr.io/b0yan-p/simple_share_backend`

`:latest` is the moving tag the deployment follows. `:sha-<short>` is immutable -
that is the one to pin when rolling back a bad `:latest`.

### Cutting a release

```bash
git checkout main && git pull
git tag v0.2.0
git push origin v0.2.0
```

Release notes are generated automatically. **They list merged pull requests
only** - commits pushed straight to `main` do not appear as entries, and are
covered only by the "Full Changelog" compare link. Work through PRs if the
changelog should be readable.

### Building locally

```bash
docker build -t simpleshare-api .
```

### Pulling

The package is private and inherits this repository's permissions, so a pull
needs a personal access token (classic) with the `read:packages` scope:

```bash
echo "$PAT" | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin
docker pull ghcr.io/b0yan-p/simple_share_backend:latest
```

Publishing itself needs no token - the workflow uses the built-in
`GITHUB_TOKEN`.

## Tech Stack

- .NET 9.0
- ASP.NET Core Web API
- Entity Framework Core 9.0
- SQL Server
- ASP.NET Core Identity
- JWT Authentication
- Swagger/OpenAPI

## Project Structure

```
simpleShare_backend/
├── Data/                    # Database context
├── Migrations/              # EF Core migrations
├── Models/                  # Domain models
│   └── Identity/            # Identity models
├── Properties/              # Launch settings
├── Program.cs               # Application entry point
├── appsettings.json         # Configuration
└── simpleShare_backend.csproj
```

## License

MIT
