#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/StudyPlanner.Web/StudyPlanner.Web.csproj"
secret_file="$root/.env.local"
dotnet_bin="${DOTNET_ROOT:-$HOME/.dotnet}/dotnet"
if [[ ! -x "$dotnet_bin" ]]; then
    dotnet_bin="$(command -v dotnet)"
fi

docker info >/dev/null 2>&1 || { echo 'Start Docker Desktop, then run this script again.' >&2; exit 1; }
"$dotnet_bin" --version >/dev/null
umask 077
if [[ ! -e "$secret_file" ]]; then
    password="$(openssl rand -hex 32)"
    (set -o noclobber; printf 'POSTGRES_PASSWORD=%s\n' "$password" > "$secret_file")
fi
password=''
while IFS='=' read -r key value; do
    if [[ "$key" == POSTGRES_PASSWORD ]]; then password="$value"; fi
done < "$secret_file"
if [[ ! "$password" =~ ^[a-f0-9]{64}$ ]]; then
    echo 'The existing .env.local does not contain the expected generated password. Nothing was overwritten.' >&2
    exit 1
fi

docker compose --env-file "$secret_file" -f "$root/deployment/compose.local.yml" up -d --wait postgres
printf '{"ConnectionStrings:DefaultConnection":"Host=127.0.0.1;Port=55432;Database=StudyPlannerDev;Username=studyplanner_local;Password=%s"}\n' "$password" |
    "$dotnet_bin" user-secrets set --project "$project" >/dev/null
printf 'Local database ready. Run the StudyPlanner.Web http profile in Rider, or:\n'
printf '  dotnet run --project src/StudyPlanner.Web --launch-profile http\n'
