#!/bin/bash
# Script to create EF Core migration

cd "$(dirname "$0")/.."

echo "Creating EF Core migration..."
dotnet ef migrations add InitialCreate \
    --project ../Infrastructure/BellNotification.Infrastructure.csproj \
    --startup-project . \
    --context ApplicationDbContext

echo "Migration created successfully!"
echo "To apply the migration, run:"
echo "  dotnet ef database update --project ../Infrastructure/BellNotification.Infrastructure.csproj --startup-project ."
