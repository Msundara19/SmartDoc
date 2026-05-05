#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ENV_FILE="$SCRIPT_DIR/.env"

if [ ! -f "$ENV_FILE" ]; then
  echo "Error: .env file not found at $ENV_FILE"
  exit 1
fi

# Parse .env safely — handles values containing semicolons, =, etc.
while IFS= read -r line || [ -n "$line" ]; do
  # Skip blank lines and comments
  [[ "$line" =~ ^[[:space:]]*$ ]] && continue
  [[ "$line" =~ ^[[:space:]]*# ]] && continue

  key="${line%%=*}"
  value="${line#*=}"

  export "$key"="$value"
done < "$ENV_FILE"

export ASPNETCORE_ENVIRONMENT=Development

echo "Starting SmartDoc API..."
echo "  DB host:    $(echo "$ConnectionStrings__DefaultConnection" | grep -o 'Host=[^;]*')"
echo "  OpenAI key: ${OpenAI__ApiKey:0:12}..."
echo "  Groq key:   ${Groq__ApiKey:0:12}..."
echo ""

cd "$SCRIPT_DIR/SmartDoc.Api"
dotnet run
