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
