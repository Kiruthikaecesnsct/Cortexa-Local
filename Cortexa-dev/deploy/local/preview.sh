#!/usr/bin/env bash
# Cortexa local preview stack - Docker Compose mode.
#
# Previews the checked-out working tree exactly as it sits on disk, including
# uncommitted changes. Performs NO git operations - it never switches
# branches, stashes, or pulls. Run it again after `git switch` yourself if you
# want the new branch's code.
#
# Cloud-backed dependencies (see services.yaml): Gemini (LLM - no local
# equivalent), Qdrant Cloud (vector-router's VECTOR_BACKEND=qdrant), Sentry
# (optional, disabled when blank). Everything else - PostgreSQL, Cosmos DB,
# Blob Storage, Service Bus - runs locally via emulators/containers.
#
# Usage:
#   preview.sh start   [--only a,b] [--skip-seed]
#   preview.sh update  [--only a,b] [--skip-seed]
#   preview.sh seed
#   preview.sh stop    [--deep-clean] [--wipe-local-volumes]
#   preview.sh reset   [--skip-seed]
#   preview.sh status
#   preview.sh logs    [service]
#   preview.sh doctor
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT_NAME="cortexa"
COMPOSE_PROJECT="${PROJECT_NAME}-preview"
ENV_FILE="$SCRIPT_DIR/.env.preview"
SERVICES_YAML="$SCRIPT_DIR/services.yaml"
CERTS_DIR="$SCRIPT_DIR/certs"
BASE_COMPOSE="$REPO_ROOT/deploy/docker-compose.yml"
PREVIEW_COMPOSE="$SCRIPT_DIR/docker-compose.preview.yml"

APP_SERVICES=(api-gateway identity job-orchestrator model-router evidence extraction harvesting ingestion scoring seeding vector-router frontend)
COSMOS_DEPENDENT_SERVICES=(job-orchestrator evidence extraction harvesting ingestion scoring seeding)
DEP_SERVICES=(postgres cosmos azurite mssql servicebus)
PUBLISHED_PORTS=(5070 5080 5081 5082 5083 5084 5085 5086 5087 5088 5089 5090 5100)

# ─────────────────────────────────────────────────────────────── logging ──
log()  { printf '[preview] %s\n' "$*"; }
warn() { printf '[preview] WARN: %s\n' "$*" >&2; }
fail() { printf '[preview] FAIL: %s\n' "$*" >&2; exit 1; }

# ──────────────────────────────────────────────────── services.yaml parse ──
# Populates parallel arrays indexed the same way: SVC_NAME[i], SVC_KIND[i], ...
# Restricted grammar per preview-contract.md - 2-space indent, one level of
# nesting, no lists/anchors. This parser only understands that grammar.
SVC_NAME=(); SVC_KIND=(); SVC_PLACEMENT=(); SVC_ENV=(); SVC_LOCAL_URL=()
SVC_LOCAL_IMAGE=(); SVC_LOCAL_UNIT=(); SVC_CLOUD_NOTE=(); SVC_REQUIRED=()
PORT_BASE=5070
PREVIEW_MODE_RECORDED=""

parse_services_yaml() {
    [ -f "$SERVICES_YAML" ] || fail "$SERVICES_YAML not found."
    local cur=-1
    while IFS= read -r line || [ -n "$line" ]; do
        [[ "$line" =~ ^#.*$ || -z "$line" ]] && continue
        if [[ "$line" =~ ^project_name:\ (.*)$ ]]; then continue
        elif [[ "$line" =~ ^preview_mode:\ (.*)$ ]]; then PREVIEW_MODE_RECORDED="${BASH_REMATCH[1]}"
        elif [[ "$line" =~ ^port_base:\ ([0-9]+)$ ]]; then PORT_BASE="${BASH_REMATCH[1]}"
        elif [[ "$line" =~ ^services:$ ]]; then continue
        elif [[ "$line" =~ ^\ \ ([a-z][a-z0-9_-]*):$ ]]; then
            cur=$((cur + 1))
            SVC_NAME[$cur]="${BASH_REMATCH[1]}"
            SVC_REQUIRED[$cur]="true"
        elif [[ "$line" =~ ^\ \ \ \ ([a-z_]+):\ (.*)$ ]]; then
            local key="${BASH_REMATCH[1]}" val="${BASH_REMATCH[2]}"
            val="${val%\'}"; val="${val#\'}"
            case "$key" in
                kind) SVC_KIND[$cur]="$val" ;;
                placement) SVC_PLACEMENT[$cur]="$val" ;;
                env) SVC_ENV[$cur]="$val" ;;
                local_url) SVC_LOCAL_URL[$cur]="$val" ;;
                local_image) SVC_LOCAL_IMAGE[$cur]="$val" ;;
                local_unit) SVC_LOCAL_UNIT[$cur]="$val" ;;
                cloud_note) SVC_CLOUD_NOTE[$cur]="$val" ;;
                required) SVC_REQUIRED[$cur]="$val" ;;
            esac
        fi
    done < "$SERVICES_YAML"

    [ "$PREVIEW_MODE_RECORDED" = "compose" ] || fail \
        "services.yaml records preview_mode: $PREVIEW_MODE_RECORDED, but preview.sh is the Compose-mode script. Run the matching script for that mode instead."

    local i
    for i in "${!SVC_NAME[@]}"; do
        case "${SVC_PLACEMENT[$i]:-}" in
            local|cloud) ;;
            *) fail "services.yaml: '${SVC_NAME[$i]}' has an unknown placement '${SVC_PLACEMENT[$i]:-}' (must be local or cloud)." ;;
        esac
        if [ "${SVC_PLACEMENT[$i]}" = "local" ] && [ -z "${SVC_LOCAL_IMAGE[$i]:-}" ] && [ -n "${SVC_LOCAL_UNIT[$i]:-}" ]; then
            # A secrets-only entry (e.g. cosmos_key) shares its local_unit with a
            # sibling entry that does carry an image - only flag it if this entry
            # is the sole one for that unit.
            local has_image=0 j
            for j in "${!SVC_NAME[@]}"; do
                [ "${SVC_LOCAL_UNIT[$j]:-}" = "${SVC_LOCAL_UNIT[$i]}" ] && [ -n "${SVC_LOCAL_IMAGE[$j]:-}" ] && has_image=1
            done
            [ "$has_image" -eq 1 ] || fail "services.yaml: '${SVC_NAME[$i]}' is placed local but its local_unit '${SVC_LOCAL_UNIT[$i]}' has no local_image anywhere."
        fi
    done
}

enabled_profiles() {
    local i seen=" "
    for i in "${!SVC_NAME[@]}"; do
        if [ "${SVC_PLACEMENT[$i]}" = "local" ] && [ -n "${SVC_LOCAL_UNIT[$i]:-}" ]; then
            if [[ "$seen" != *" ${SVC_LOCAL_UNIT[$i]} "* ]]; then
                printf '%s ' "${SVC_LOCAL_UNIT[$i]}"
                seen="$seen${SVC_LOCAL_UNIT[$i]} "
            fi
        fi
    done
}

# ─────────────────────────────────────────────────────────────── compose ──
compose() {
    local profile_args=()
    local p
    for p in $(enabled_profiles); do profile_args+=(--profile "$p"); done
    docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" \
        -f "$BASE_COMPOSE" -f "$PREVIEW_COMPOSE" "${profile_args[@]}" "$@"
}

# ───────────────────────────────────────────────────────────── runtime ──
assert_runtime() {
    command -v docker >/dev/null 2>&1 || fail "Docker is not installed or not on PATH."
    docker info >/dev/null 2>&1 || fail "Docker daemon is not reachable. Is Docker Desktop running?"
    local ver major minor
    ver="$(docker compose version --short 2>/dev/null || echo 0.0.0)"
    major="$(echo "$ver" | cut -d. -f1)"; minor="$(echo "$ver" | cut -d. -f2)"
    if [ "$major" -lt 2 ] || { [ "$major" -eq 2 ] && [ "$minor" -lt 24 ]; }; then
        fail "Docker Compose $ver found; this stack needs >= 2.24 (uses !override/!reset). Update Docker Desktop."
    fi
}

# ─────────────────────────────────────────────────────────────── env ──
check_env() {
    [ -f "$ENV_FILE" ] || fail \
        "$ENV_FILE not found. Copy .env.preview.example to .env.preview and fill in the cloud keys, then re-run."

    # shellcheck disable=SC1090
    get_env_val() { grep -E "^$1=" "$ENV_FILE" 2>/dev/null | tail -n1 | cut -d= -f2- ; }

    local i missing=0
    for i in "${!SVC_NAME[@]}"; do
        [ "${SVC_PLACEMENT[$i]}" = "cloud" ] || continue
        [ "${SVC_REQUIRED[$i]}" = "false" ] && continue
        local val; val="$(get_env_val "${SVC_ENV[$i]}")"
        if [ -z "${val:-}" ]; then
            warn "Missing cloud config: ${SVC_NAME[$i]} needs ${SVC_ENV[$i]} in $ENV_FILE (${SVC_CLOUD_NOTE[$i]:-})"
            missing=1
        fi
    done
    [ "$missing" -eq 0 ] || fail "One or more required cloud credentials are missing. See warnings above."

    if [[ " $(enabled_profiles) " == *" servicebus "* ]]; then
        local eula; eula="$(get_env_val ACCEPT_EULA)"
        [ "$eula" = "Y" ] || fail \
            "ACCEPT_EULA must be Y in $ENV_FILE to start the Service Bus emulator and its SQL Server Linux companion. See the EULA links in .env.preview.example."
    fi
}

# ─────────────────────────────────────────────────────────────── ports ──
port_busy() {
    (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null && { exec 3<&- 3>&-; return 0; }
    return 1
}

check_ports() {
    local p busy=0
    for p in "${PUBLISHED_PORTS[@]}"; do
        if port_busy "$p"; then
            local occupier
            occupier="$(docker ps --filter "publish=$p" --format '{{.Names}}' 2>/dev/null | head -n1)"
            if [ -n "$occupier" ]; then
                log "Port $p is already used by container '$occupier' - assuming it's this stack, continuing."
            else
                warn "Port $p is in use by something outside this stack."
                busy=1
            fi
        fi
    done
    [ "$busy" -eq 0 ] || fail "One or more preview ports are occupied by a non-preview process. Free them or edit port_base in services.yaml."
}

# ─────────────────────────────────────────────── export local placements ──
export_local_env() {
    local i
    for i in "${!SVC_NAME[@]}"; do
        [ "${SVC_PLACEMENT[$i]}" = "local" ] || continue
        export "${SVC_ENV[$i]}=${SVC_LOCAL_URL[$i]}"
    done
}

# ────────────────────────────────────────────────────── wait_healthy ──
wait_healthy() {
    local svc="$1" timeout="${2:-180}" waited=0
    log "Waiting for $svc to be healthy (timeout ${timeout}s)..."
    while [ "$waited" -lt "$timeout" ]; do
        local cid status
        cid="$(compose ps -q "$svc" 2>/dev/null || true)"
        if [ -n "$cid" ]; then
            status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$cid" 2>/dev/null || echo unknown)"
            [ "$status" = "healthy" ] && { log "$svc is healthy."; return 0; }
            [ "$status" = "none" ] && { log "$svc has no healthcheck; assuming ready (running)."; return 0; }
        fi
        sleep 3; waited=$((waited + 3))
    done
    fail "$svc did not become healthy within ${timeout}s. Check logs: $0 logs $svc"
}

# ────────────────────────────────────────────────── init_cosmos_cert ──
# Fetches the Cosmos DB emulator's self-signed TLS certificate once it is
# healthy, so deploy/local/scripts/dev-entrypoint.sh can import it into every
# Cosmos-dependent container's trust store before the app starts. See that
# script's header for why this is necessary and the official doc it follows.
init_cosmos_cert() {
    [[ " $(enabled_profiles) " == *" cosmos "* ]] || return 0
    mkdir -p "$CERTS_DIR"
    log "Fetching the Cosmos DB emulator's TLS certificate..."
    if docker run --rm --network cortexa-network curlimages/curl:latest \
        -sk "https://cosmos:8081/_explorer/emulator.pem" -o /tmp/cosmos-emulator.pem 2>/dev/null; then
        docker run --rm --network cortexa-network -v "$CERTS_DIR:/out" \
            curlimages/curl:latest -sk "https://cosmos:8081/_explorer/emulator.pem" -o /out/cosmos-emulator.pem
        log "Certificate saved to $CERTS_DIR/cosmos-emulator.pem"
    else
        warn "Could not fetch the Cosmos emulator certificate. Cosmos-dependent services may fail TLS verification - see README troubleshooting."
    fi
}

# ─────────────────────────────────────────────────────────────── actions ──
ONLY=""; SKIP_SEED=0; DEEP_CLEAN=0; WIPE_VOLUMES=0

do_start() {
    assert_runtime
    parse_services_yaml
    check_env
    check_ports
    export_local_env

    log "Starting dependencies (${DEP_SERVICES[*]})..."
    local deps_enabled=()
    local d
    for d in "${DEP_SERVICES[@]}"; do
        case "$d" in
            postgres) [[ " $(enabled_profiles) " == *" postgres "* ]] && deps_enabled+=("$d") ;;
            cosmos) [[ " $(enabled_profiles) " == *" cosmos "* ]] && deps_enabled+=("$d") ;;
            azurite) [[ " $(enabled_profiles) " == *" blob "* ]] && deps_enabled+=("$d") ;;
            mssql|servicebus) [[ " $(enabled_profiles) " == *" servicebus "* ]] && deps_enabled+=("$d") ;;
        esac
    done
    [ "${#deps_enabled[@]}" -gt 0 ] && compose up -d --build "${deps_enabled[@]}"
    for d in "${deps_enabled[@]}"; do wait_healthy "$d" 180; done

    init_cosmos_cert

    log "Building and starting app services..."
    if [ -n "$ONLY" ]; then
        IFS=',' read -ra only_list <<< "$ONLY"
        compose up -d --build "${only_list[@]}"
    else
        compose up -d --build
    fi

    local s
    for s in "${APP_SERVICES[@]}"; do
        [ -n "$ONLY" ] && [[ ",$ONLY," != *",$s,"* ]] && continue
        wait_healthy "$s" 180
    done

    log "Migrations: identity applies its own EF Core migrations on startup - nothing further to run."

    if [ "$SKIP_SEED" -eq 0 ]; then
        do_seed
    else
        log "Skipping seed (--skip-seed)."
    fi

    print_banner
}

do_update() {
    assert_runtime
    parse_services_yaml
    check_env
    export_local_env
    log "Rebuilding and rolling changed services..."
    if [ -n "$ONLY" ]; then
        IFS=',' read -ra only_list <<< "$ONLY"
        compose up -d --build "${only_list[@]}"
        compose restart "${only_list[@]}"
    else
        compose up -d --build
        compose restart "${APP_SERVICES[@]}"
    fi
    [ "$SKIP_SEED" -eq 1 ] || do_seed
    print_banner
}

do_seed() {
    log "No seed script exists in this project (no scripts/seed.* found under any service). Skipping - nothing to do."
}

do_stop() {
    parse_services_yaml
    if [ "$WIPE_VOLUMES" -eq 1 ]; then
        local i
        for i in "${!SVC_NAME[@]}"; do
            [ "${SVC_PLACEMENT[$i]}" = "cloud" ] && fail \
                "--wipe-local-volumes refused: '${SVC_NAME[$i]}' is placed cloud. Wiping local volumes never touches a cloud-placed dependency's data, and this stack has no way to distinguish which volumes are safe without your confirmation below."
        done
        warn "This deletes all local Postgres/Cosmos/Blob data for the $COMPOSE_PROJECT stack. Cannot be undone."
        read -r -p "Type the project name ($COMPOSE_PROJECT) to confirm: " confirm
        [ "$confirm" = "$COMPOSE_PROJECT" ] || fail "Confirmation did not match. Aborting - no volumes removed."
        compose down --remove-orphans
        local vols
        vols="$(docker volume ls -q --filter "label=com.docker.compose.project=$COMPOSE_PROJECT")"
        [ -n "$vols" ] && echo "$vols" | xargs -r docker volume rm
        log "Volumes removed."
        return
    fi
    if [ "$DEEP_CLEAN" -eq 1 ]; then
        compose down --rmi local --remove-orphans
    else
        compose down --remove-orphans
    fi
    log "Stopped. State and built images are kept unless --deep-clean or --wipe-local-volumes was passed."
}

do_reset() {
    do_stop
    do_start
}

do_status() { parse_services_yaml; compose ps; }

do_logs() {
    parse_services_yaml
    if [ -n "${1:-}" ]; then compose logs -f --tail 200 "$1"; else compose logs -f --tail 200; fi
}

do_doctor() {
    local fail_count=0
    echo "Docker:"
    if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
        echo "  OK   Docker daemon reachable."
    else
        echo "  FAIL Docker not installed or daemon unreachable."; fail_count=$((fail_count+1))
    fi
    local ver; ver="$(docker compose version --short 2>/dev/null || echo 0.0.0)"
    echo "  Compose version: $ver $( [ "$(echo "$ver" | cut -d. -f1)" -ge 2 ] && echo OK || echo 'FAIL (need >= 2.24)')"

    parse_services_yaml

    echo "Env file:"
    if [ -f "$ENV_FILE" ]; then
        echo "  OK   $ENV_FILE exists."
        local i
        for i in "${!SVC_NAME[@]}"; do
            [ "${SVC_PLACEMENT[$i]}" = "cloud" ] || continue
            local val; val="$(grep -E "^${SVC_ENV[$i]}=" "$ENV_FILE" 2>/dev/null | tail -n1 | cut -d= -f2-)"
            if [ -z "${val:-}" ]; then
                if [ "${SVC_REQUIRED[$i]}" = "false" ]; then
                    echo "  WARN ${SVC_ENV[$i]} empty (${SVC_NAME[$i]}, optional)."
                else
                    echo "  FAIL ${SVC_ENV[$i]} empty (${SVC_NAME[$i]}) - $ENV_FILE"; fail_count=$((fail_count+1))
                fi
            else
                echo "  OK   ${SVC_ENV[$i]} set (${SVC_NAME[$i]})."
            fi
        done
    else
        echo "  FAIL $ENV_FILE missing - copy .env.preview.example."; fail_count=$((fail_count+1))
    fi

    echo "Ports:"
    local p
    for p in "${PUBLISHED_PORTS[@]}"; do
        if port_busy "$p"; then echo "  WARN port $p in use"; else echo "  OK   port $p free"; fi
    done

    echo "Placement table:"
    local i
    for i in "${!SVC_NAME[@]}"; do
        printf '  %-14s %-7s %s\n' "${SVC_NAME[$i]}" "${SVC_PLACEMENT[$i]}" "${SVC_ENV[$i]:-}"
    done

    echo "Workload status:"
    compose ps 2>/dev/null || echo "  (stack not running)"

    if [ "$fail_count" -gt 0 ]; then
        echo "doctor: $fail_count FAIL(s)."; exit 1
    fi
    echo "doctor: all checks OK."
}

print_banner() {
    local branch
    branch="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)"
    cat <<EOF

────────────────────────────────────────────────────────────────────────────
Cortexa preview stack is up (branch: $branch)

  Frontend            http://localhost:5070
  api-gateway         http://localhost:5080
  identity            http://localhost:5081
  job-orchestrator    http://localhost:5082
  model-router        http://localhost:5083
  evidence            http://localhost:5084
  extraction          http://localhost:5085
  harvesting          http://localhost:5086
  ingestion           http://localhost:5087
  scoring             http://localhost:5088
  seeding             http://localhost:5089
  vector-router       http://localhost:5090
  Cosmos Data Explorer http://localhost:5100

Code hot-reloads on save. Run '$0 update' after changing a Dockerfile,
lockfile, or dependency version - hot reload alone won't pick those up.

On a remote or tunnelled host, replace localhost above with the forwarded URL.
────────────────────────────────────────────────────────────────────────────
EOF
}

# ─────────────────────────────────────────────────────────────── main ──
ACTION="${1:-}"; shift || true
while [ $# -gt 0 ]; do
    case "$1" in
        --only) ONLY="$2"; shift 2 ;;
        --skip-seed) SKIP_SEED=1; shift ;;
        --deep-clean) DEEP_CLEAN=1; shift ;;
        --wipe-local-volumes) WIPE_VOLUMES=1; shift ;;
        *) SERVICE_ARG="${SERVICE_ARG:-$1}"; shift ;;
    esac
done

case "$ACTION" in
    start) do_start ;;
    update) do_update ;;
    seed) parse_services_yaml; export_local_env; do_seed ;;
    stop) do_stop ;;
    reset) do_reset ;;
    status) do_status ;;
    logs) do_logs "${SERVICE_ARG:-}" ;;
    doctor) do_doctor ;;
    *) fail "Usage: $0 {start|update|seed|stop|reset|status|logs|doctor} [options]" ;;
esac
