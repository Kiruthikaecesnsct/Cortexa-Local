#!/usr/bin/env bash
# manageboards.sh — Push a User Story or Bug JSON (or a folder of them) to Azure DevOps Boards.
# Usage:
#   ./manageboards.sh --userstory --json <path> [--project <name>] [--assignee <email>]
#   ./manageboards.sh --bug       --json <path> [--project <name>] [--assignee <email>]
#   ./manageboards.sh --json <folder> [--force]
#
# All flags except --json can be sourced from .azure/config.json in the repo root.
# --project and --assignee on the command line override config.json values.
#
# When --json points to a folder, every *.json file under it is scanned, validated
# (must have a 'Title'), and pushed one by one. Type is auto-detected per file from
# 'workItemType' or the US*/BUG* filename prefix.
#
# Every push is recorded in boards_tracker.csv (next to this script) mapping the
# work-item key (e.g. US001, BUG012) to the created Azure DevOps ID. On re-run,
# items already present in the tracker are skipped unless --force is passed.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_FILE="${SCRIPT_DIR}/config.json"
TRACKER_FILE="${SCRIPT_DIR}/boards_tracker.csv"

# ── helpers ──────────────────────────────────────────────────────────────────

die()  { echo "❌ ERROR: $*" >&2; exit 1; }
info() { echo "   $*"; }
ok()   { echo "✅ $*"; }

require_cmd() { command -v "$1" &>/dev/null || die "$1 is required but not installed."; }

# ── arg parsing ───────────────────────────────────────────────────────────────

WORK_ITEM_TYPE=""
JSON_PATH=""
ARG_PROJECT=""
ARG_ASSIGNEE=""
FORCE="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --userstory) WORK_ITEM_TYPE="User Story"; shift ;;
    --bug)       WORK_ITEM_TYPE="Bug";        shift ;;
    --json)      JSON_PATH="$2";              shift 2 ;;
    --project)   ARG_PROJECT="$2";            shift 2 ;;
    --assignee)  ARG_ASSIGNEE="$2";           shift 2 ;;
    --force)     FORCE="true";                shift ;;
    *) die "Unknown argument: $1. Use --userstory|--bug --json <path|folder> [--project <name>] [--assignee <email>] [--force]" ;;
  esac
done

# ── load config ───────────────────────────────────────────────────────────────

require_cmd jq

[[ -f "$CONFIG_FILE" ]] || die "config.json not found at $CONFIG_FILE. Copy config.json.example → config.json and fill it in."

cfg() { jq -r "$1 // empty" "$CONFIG_FILE"; }

ORGANIZATION=$(cfg '.organization')
CFG_PROJECT=$(cfg '.project')
CFG_ASSIGNEE=$(cfg '.defaultAssignee')
TEAM=$(cfg '.team')
PAT=$(cfg '.pat')

[[ -n "$ORGANIZATION" ]] || die "config.json: 'organization' is required."
[[ -n "$PAT" ]]          || die "config.json: 'pat' is required."

PROJECT="${ARG_PROJECT:-$CFG_PROJECT}"
ASSIGNEE="${ARG_ASSIGNEE:-$CFG_ASSIGNEE}"

[[ -n "$PROJECT" ]] || die "'project' must be set via --project or config.json."

# ── validate inputs ───────────────────────────────────────────────────────────

[[ -n "$JSON_PATH" ]] || die "--json <path> is required."
[[ -e "$JSON_PATH" ]] || die "Path not found: $JSON_PATH"

if [[ -f "$JSON_PATH" ]]; then
  # Single-file mode: resolve type upfront (unchanged behavior)
  if [[ -z "$WORK_ITEM_TYPE" ]]; then
    WORK_ITEM_TYPE=$(jq -r '.workItemType // empty' "$JSON_PATH")
    [[ -n "$WORK_ITEM_TYPE" ]] || die "Could not determine work item type. Pass --userstory or --bug, or add 'workItemType' to the JSON."
  fi
  [[ "$WORK_ITEM_TYPE" == "User Story" || "$WORK_ITEM_TYPE" == "Bug" ]] \
    || die "Unsupported work item type: '$WORK_ITEM_TYPE'. Expected 'User Story' or 'Bug'."
fi
# Folder mode: type is resolved per-file inside push_one_json

require_cmd curl

# ── auth header ───────────────────────────────────────────────────────────────

AUTH_HEADER="Authorization: Basic $(echo -n ":${PAT}" | base64 | tr -d '\n')"
BASE_URL="https://dev.azure.com/${ORGANIZATION}/${PROJECT}/_apis/wit"
API_VER="api-version=7.0"

# ── connection test ───────────────────────────────────────────────────────────

echo "🔗 Testing connection to Azure DevOps..."
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" \
  -H "$AUTH_HEADER" \
  "${BASE_URL}/workitemtypes?${API_VER}")

[[ "$HTTP_STATUS" == "200" ]] || die "Connection failed (HTTP $HTTP_STATUS). Check organization, project, and PAT."
ok "Connected — org: $ORGANIZATION | project: $PROJECT"

# ── field builder helpers ─────────────────────────────────────────────────────

# Wraps plain text in HTML for ADO rich-text fields.
# Uses <p> (not <div>) — TinyMCE (used by Description) requires <p> to render.
# Each newline becomes a new <p> block so multi-line content renders correctly.
to_html() {
  local text="$1"
  text="${text//\"/&quot;}"
  # Split on newlines: each non-empty line becomes its own <p>
  local result=""
  while IFS= read -r line; do
    [[ -n "$line" ]] && result+="<p>${line}</p>"
  done <<< "$text"
  # Fallback: if no newlines, wrap the whole thing
  [[ -z "$result" ]] && result="<p>${text}</p>"
  echo "$result"
}

# Appends a JSON-patch operation to OPERATIONS array (bash nameref)
add_op() {
  local path="$1" value="$2" type="${3:-string}"
  if [[ "$type" == "number" ]]; then
    OPERATIONS+=("{\"op\":\"add\",\"path\":\"$path\",\"value\":$value}")
  else
    # Escape backslashes first, then quotes, then newlines for valid JSON strings
    local escaped="${value//\\/\\\\}"
    escaped="${escaped//\"/\\\"}"
    escaped="${escaped//$'\n'/\\n}"
    OPERATIONS+=("{\"op\":\"add\",\"path\":\"$path\",\"value\":\"$escaped\"}")
  fi
}

create_work_item() {
  local wi_type_encoded="${1// /%20}"
  local body="$2"
  curl -s -X POST \
    -H "$AUTH_HEADER" \
    -H "Content-Type: application/json-patch+json" \
    -d "$body" \
    "${BASE_URL}/workitems/\$${wi_type_encoded}?${API_VER}"
}

patch_work_item() {
  local wi_id="$1" body="$2"
  curl -s -X PATCH \
    -H "$AUTH_HEADER" \
    -H "Content-Type: application/json-patch+json" \
    -d "$body" \
    "${BASE_URL}/workitems/${wi_id}?${API_VER}"
}

link_child_to_parent() {
  local child_id="$1" parent_id="$2"
  local body="[{\"op\":\"add\",\"path\":\"/relations/-\",\"value\":{\"rel\":\"System.LinkTypes.Hierarchy-Reverse\",\"url\":\"https://dev.azure.com/${ORGANIZATION}/${PROJECT}/_apis/wit/workItems/${parent_id}\"}}]"
  patch_work_item "$child_id" "$body" > /dev/null
}

# ── create User Story ─────────────────────────────────────────────────────────

create_user_story() {
  local json_file="$1"
  echo "📝 Creating User Story..."

  local title description acceptance_criteria story_points priority remaining_work tags talking_points
  title=$(jq -r '.Title // empty' "$json_file")
  description=$(jq -r '.Description // empty' "$json_file")
  acceptance_criteria=$(jq -r 'if .AcceptanceCriteria | type == "array" then .AcceptanceCriteria | map("• " + .) | join("\n") else .AcceptanceCriteria // "" end' "$json_file")
  story_points=$(jq -r '.Planning.StoryPoints // empty' "$json_file")
  priority=$(jq -r '.Planning.Priority // empty' "$json_file")
  remaining_work=$(jq -r '.Classification.RemainingWork // empty' "$json_file")
  tags=$(jq -r 'if .Tags | type == "array" then .Tags | join("; ") else .Tags // "" end' "$json_file")
  talking_points=$(jq -r '.TalkingPoints // empty' "$json_file")

  if [[ -z "$title" ]]; then
    echo "   ⚠ Skipping — User Story JSON must have a 'Title' field." >&2
    return 1
  fi

  OPERATIONS=()
  add_op "/fields/System.Title" "$title"
  [[ -n "$description" ]]         && add_op "/fields/System.Description" "$(to_html "$description")"
  [[ -n "$acceptance_criteria" ]] && add_op "/fields/Microsoft.VSTS.Common.AcceptanceCriteria" "$(to_html "$acceptance_criteria")"
  [[ -n "$story_points" ]]        && add_op "/fields/Microsoft.VSTS.Scheduling.StoryPoints" "$story_points" number
  [[ -n "$priority" ]]            && add_op "/fields/Microsoft.VSTS.Common.Priority" "$priority" number
  [[ -n "$remaining_work" ]]      && add_op "/fields/Microsoft.VSTS.Scheduling.RemainingWork" "$remaining_work" number
  [[ -n "$tags" ]]                && add_op "/fields/System.Tags" "$tags"
  [[ -n "$talking_points" ]]      && add_op "/fields/System.History" "$(to_html "$talking_points")"
  [[ -n "$ASSIGNEE" ]]            && add_op "/fields/System.AssignedTo" "$ASSIGNEE"

  local body="[$(IFS=,; echo "${OPERATIONS[*]}")]"
  local response
  response=$(create_work_item "User Story" "$body")

  local wi_id wi_url
  wi_id=$(echo "$response" | jq -r '.id // empty')
  if [[ -z "$wi_id" ]]; then
    echo "   ❌ Failed to create User Story. API response: $response" >&2
    return 1
  fi

  wi_url="https://dev.azure.com/${ORGANIZATION}/${PROJECT}/_workitems/edit/${wi_id}"
  ok "User Story created"
  info "ID   : $wi_id"
  info "Title: $title"
  info "URL  : $wi_url"

  # Create child Tasks if present
  local task_count
  task_count=$(jq '.Tasks | length // 0' "$json_file" 2>/dev/null || echo 0)
  if [[ "$task_count" -gt 0 ]]; then
    echo ""
    echo "📋 Creating $task_count child Task(s)..."
    local success=0 failed=0
    for i in $(seq 0 $((task_count - 1))); do
      local task_json
      task_json=$(jq ".Tasks[$i]" "$json_file")
      if create_task "$task_json" "$wi_id"; then ((++success)) || true; else ((++failed)) || true; fi
    done
    info "Tasks created: $success | failed: $failed"
  fi

  # Create linked Test Cases if present
  local test_count
  test_count=$(jq '.Tests | length // 0' "$json_file" 2>/dev/null || echo 0)
  if [[ "$test_count" -gt 0 ]]; then
    echo ""
    echo "🧪 Creating $test_count Test Case(s)..."
    local tc_success=0 tc_failed=0
    for i in $(seq 0 $((test_count - 1))); do
      local test_json
      test_json=$(jq ".Tests[$i]" "$json_file")
      if create_test_case "$test_json" "$wi_id"; then ((++tc_success)) || true; else ((++tc_failed)) || true; fi
    done
    info "Test Cases created: $tc_success | failed: $tc_failed"
  fi

  LAST_WI_ID="$wi_id"
  LAST_WI_URL="$wi_url"
}

# ── create Task ───────────────────────────────────────────────────────────────

create_task() {
  local task_json="$1" parent_id="$2"

  local task_title task_description task_priority task_activity task_estimate task_tags task_assignee
  task_title=$(echo "$task_json" | jq -r '.Title // empty')
  task_description=$(echo "$task_json" | jq -r '.Description // empty')
  task_priority=$(echo "$task_json" | jq -r '.Planning.Priority // empty')
  task_activity=$(echo "$task_json" | jq -r '.Planning.Activity // empty')
  task_estimate=$(echo "$task_json" | jq -r '.Efforts.OriginalEstimate // empty')
  task_tags=$(echo "$task_json" | jq -r 'if .Tags | type == "array" then .Tags | join("; ") else .Tags // "" end')
  task_assignee=$(echo "$task_json" | jq -r '.AssignedTo // empty')
  [[ -z "$task_assignee" ]] && task_assignee="$ASSIGNEE"

  [[ -n "$task_title" ]] || { echo "   ⚠ Skipping task with missing Title" >&2; return 1; }

  OPERATIONS=()
  add_op "/fields/System.Title" "$task_title"
  [[ -n "$task_description" ]] && add_op "/fields/System.Description" "$(to_html "$task_description")"
  [[ -n "$task_assignee" ]]    && add_op "/fields/System.AssignedTo" "$task_assignee"

  local body="[$(IFS=,; echo "${OPERATIONS[*]}")]"
  local response
  response=$(create_work_item "Task" "$body")

  local task_id
  task_id=$(echo "$response" | jq -r '.id // empty')
  [[ -n "$task_id" ]] || { echo "   ❌ Failed to create task: $task_title" >&2; return 1; }

  sleep 1

  # Second pass: update optional fields
  OPERATIONS=()
  [[ -n "$task_tags" ]]     && add_op "/fields/System.Tags" "$task_tags"
  [[ -n "$task_priority" ]] && add_op "/fields/Microsoft.VSTS.Common.Priority" "$task_priority" number
  [[ -n "$task_activity" ]] && add_op "/fields/Microsoft.VSTS.Common.Activity" "$task_activity"
  [[ -n "$task_estimate" ]] && add_op "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate" "$task_estimate" number

  if [[ "${#OPERATIONS[@]}" -gt 0 ]]; then
    local update_body="[$(IFS=,; echo "${OPERATIONS[*]}")]"
    patch_work_item "$task_id" "$update_body" > /dev/null
  fi

  link_child_to_parent "$task_id" "$parent_id"
  echo "   ✅ Task: $task_title (ID: $task_id)"
}

# ── create Test Case ─────────────────────────────────────────────────────────

create_test_case() {
  local test_json="$1" parent_id="$2"

  local tc_title step_count steps_xml
  tc_title=$(echo "$test_json" | jq -r '.Title // empty')
  [[ -n "$tc_title" ]] || { echo "   ⚠ Skipping test case with missing Title" >&2; return 1; }

  # Build ADO steps XML from Steps array
  step_count=$(echo "$test_json" | jq '.Steps | length // 0')
  steps_xml="<steps id=\"0\" last=\"${step_count}\">"
  for s in $(seq 0 $((step_count - 1))); do
    local step_num action expected
    step_num=$((s + 1))
    action=$(echo "$test_json" | jq -r ".Steps[$s].Action // empty" | sed 's/&/\&amp;/g; s/</\&lt;/g; s/>/\&gt;/g; s/"/\&quot;/g')
    expected=$(echo "$test_json" | jq -r ".Steps[$s].Expected // empty" | sed 's/&/\&amp;/g; s/</\&lt;/g; s/>/\&gt;/g; s/"/\&quot;/g')
    steps_xml+="<step id=\"${step_num}\" type=\"ValidateStep\"><parameterizedString isformatted=\"true\">&lt;DIV&gt;${action}&lt;/DIV&gt;</parameterizedString><parameterizedString isformatted=\"true\">&lt;DIV&gt;${expected}&lt;/DIV&gt;</parameterizedString><description/></step>"
  done
  steps_xml+="</steps>"

  # Create the Test Case work item
  OPERATIONS=()
  add_op "/fields/System.Title" "$tc_title"
  [[ -n "$ASSIGNEE" ]] && add_op "/fields/System.AssignedTo" "$ASSIGNEE"

  local body
  body="[$(IFS=,; echo "${OPERATIONS[*]}")]"
  local response tc_id
  response=$(create_work_item "Test Case" "$body")
  tc_id=$(echo "$response" | jq -r '.id // empty')
  [[ -n "$tc_id" ]] || { echo "   ❌ Failed to create test case: $tc_title" >&2; return 1; }

  sleep 1

  # Patch steps XML
  local steps_escaped
  steps_escaped="${steps_xml//\"/\\\"}"
  local steps_body="[{\"op\":\"add\",\"path\":\"/fields/Microsoft.VSTS.TCM.Steps\",\"value\":\"${steps_escaped}\"}]"
  patch_work_item "$tc_id" "$steps_body" > /dev/null

  # Link test case to user story via TestedBy relationship
  local link_body="[{\"op\":\"add\",\"path\":\"/relations/-\",\"value\":{\"rel\":\"Microsoft.VSTS.Common.TestedBy-Reverse\",\"url\":\"https://dev.azure.com/${ORGANIZATION}/${PROJECT}/_apis/wit/workItems/${parent_id}\"}}]"
  patch_work_item "$tc_id" "$link_body" > /dev/null

  echo "   ✅ Test Case: $tc_title (ID: $tc_id)"
}

# ── create Bug ────────────────────────────────────────────────────────────────

create_bug() {
  local json_file="$1"
  echo "🐛 Creating Bug..."

  local title description repro_steps priority severity effort_hours discussion
  title=$(jq -r '.Title // empty' "$json_file")

  # Description must be a plain string. If an agent wrote it as an object, flatten it.
  if jq -e '.Description | type == "object"' "$json_file" > /dev/null 2>&1; then
    description=$(jq -r '[
      ("Current behaviour: " + (.Description["Current behaviour"] // "")),
      ("Expected behaviour: " + (.Description["Expected behaviour"] // "")),
      ("Affected files: "     + (.Description["Affected files"]     // "")),
      ("Impact: "             + (.Description["Impact"]             // ""))
    ] | join("\n")' "$json_file")
  else
    description=$(jq -r '.Description // empty' "$json_file")
  fi

  # ReproSteps must be a plain string. If an agent wrote it as an array, join the items.
  if jq -e '.ReproSteps | type == "array"' "$json_file" > /dev/null 2>&1; then
    repro_steps=$(jq -r '.ReproSteps | join("\n")' "$json_file")
  else
    repro_steps=$(jq -r '.ReproSteps // empty' "$json_file")
  fi
  priority=$(jq -r '.Planning.Priority // empty' "$json_file")
  severity=$(jq -r '.Planning.Severity // empty' "$json_file")
  effort_hours=$(jq -r '.Efforts.OriginalEstimate // empty' "$json_file")
  discussion=$(jq -r '.Discussion // empty' "$json_file")

  if [[ -z "$title" ]]; then
    echo "   ⚠ Skipping — Bug JSON must have a 'Title' field." >&2
    return 1
  fi

  OPERATIONS=()
  add_op "/fields/System.Title" "$title"
  [[ -n "$description" ]]  && add_op "/fields/System.Description" "$(to_html "$description")"
  [[ -n "$repro_steps" ]]  && add_op "/fields/Microsoft.VSTS.TCM.ReproSteps" "$(to_html "$repro_steps")"
  [[ -n "$ASSIGNEE" ]]     && add_op "/fields/System.AssignedTo" "$ASSIGNEE"

  local body="[$(IFS=,; echo "${OPERATIONS[*]}")]"
  local response
  response=$(create_work_item "Bug" "$body")

  local wi_id
  wi_id=$(echo "$response" | jq -r '.id // empty')
  if [[ -z "$wi_id" ]]; then
    echo "   ❌ Failed to create Bug. API response: $response" >&2
    return 1
  fi

  sleep 2

  # Second pass: update fields the ADO API requires after creation
  OPERATIONS=()
  [[ -n "$priority" ]] && add_op "/fields/Microsoft.VSTS.Common.Priority" "$priority" number

  if [[ -n "$severity" ]]; then
    declare -A SEV_MAP=(["1"]="1 - Critical" ["2"]="2 - High" ["3"]="3 - Medium" ["4"]="4 - Low")
    local sev_label="${SEV_MAP[$severity]:-}"
    [[ -n "$sev_label" ]] && add_op "/fields/Microsoft.VSTS.Common.Severity" "$sev_label"
  fi

  [[ -n "$effort_hours" ]] && add_op "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate" "$effort_hours" number
  [[ -n "$discussion" ]]   && add_op "/fields/System.History" "$(to_html "$discussion")"

  if [[ "${#OPERATIONS[@]}" -gt 0 ]]; then
    local update_body="[$(IFS=,; echo "${OPERATIONS[*]}")]"
    patch_work_item "$wi_id" "$update_body" > /dev/null
  fi

  local wi_url="https://dev.azure.com/${ORGANIZATION}/${PROJECT}/_workitems/edit/${wi_id}"
  ok "Bug created"
  info "ID   : $wi_id"
  info "Title: $title"
  info "URL  : $wi_url"

  LAST_WI_ID="$wi_id"
  LAST_WI_URL="$wi_url"
}

# ── tracker (US/BUG number → ADO work item id) ────────────────────────────────

derive_key() {
  local base
  base="$(basename "$1" .json)"
  if [[ "$base" =~ ^([Uu][Ss]|[Bb][Uu][Gg])[-_]?([0-9]+) ]]; then
    echo "${BASH_REMATCH[1]^^}${BASH_REMATCH[2]}"
  else
    echo "$base"
  fi
}

tracker_lookup() {
  local key="$1"
  [[ -f "$TRACKER_FILE" ]] || return 1
  awk -F',' -v k="$key" '$1==k {print $2; found=1} END{exit !found}' "$TRACKER_FILE"
}

tracker_upsert() {
  local key="$1" ado_id="$2" wi_type="$3" title="$4" url="$5" src="$6"
  local ts tmp
  ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  title="${title//,/;}"; title="${title//$'\n'/ }"
  src="${src//,/;}"
  tmp="${TRACKER_FILE}.tmp"
  if [[ -f "$TRACKER_FILE" ]]; then
    grep -v "^${key}," "$TRACKER_FILE" > "$tmp" || true
  else
    echo "WorkItemKey,AdoId,Type,Title,Url,SourceFile,PushedAt" > "$tmp"
  fi
  echo "${key},${ado_id},${wi_type},${title},${url},${src},${ts}" >> "$tmp"
  mv "$tmp" "$TRACKER_FILE"
}

is_valid_workitem_json() {
  local f="$1"
  jq -e . "$f" >/dev/null 2>&1 || return 1
  jq -e '(.Title // "") | length > 0' "$f" >/dev/null 2>&1 || return 1
  return 0
}

# Pushes a single JSON file. Returns 0 on success, 1 on failure, 2 if skipped
# (already present in the tracker and --force not given).
push_one_json() {
  local json_file="$1" type_override="$2"
  local key
  key="$(derive_key "$json_file")"

  if [[ "$FORCE" != "true" ]]; then
    local existing_id
    existing_id="$(tracker_lookup "$key" || true)"
    if [[ -n "$existing_id" ]]; then
      echo "⏭  Skipping $json_file — already pushed as $key (ADO ID: $existing_id). Use --force to re-push."
      return 2
    fi
  fi

  local wi_type="$type_override"
  [[ -n "$wi_type" ]] || wi_type=$(jq -r '.workItemType // empty' "$json_file")
  if [[ -z "$wi_type" ]]; then
    case "$key" in
      US*)  wi_type="User Story" ;;
      BUG*) wi_type="Bug" ;;
    esac
  fi
  if [[ "$wi_type" != "User Story" && "$wi_type" != "Bug" ]]; then
    echo "⚠ Skipping $json_file — cannot determine work item type." >&2
    return 1
  fi

  LAST_WI_ID=""
  LAST_WI_URL=""
  if [[ "$wi_type" == "User Story" ]]; then
    if ! create_user_story "$json_file"; then
      echo "❌ Failed to push $json_file" >&2
      return 1
    fi
  else
    if ! create_bug "$json_file"; then
      echo "❌ Failed to push $json_file" >&2
      return 1
    fi
  fi

  local title
  title=$(jq -r '.Title // empty' "$json_file")
  tracker_upsert "$key" "$LAST_WI_ID" "$wi_type" "$title" "$LAST_WI_URL" "$json_file"
  echo "$LAST_WI_URL"
}

# ── dispatch ──────────────────────────────────────────────────────────────────

echo ""

if [[ -d "$JSON_PATH" ]]; then
  echo "📂 Scanning folder: $JSON_PATH"
  mapfile -t JSON_FILES < <(find "$JSON_PATH" -type f -iname '*.json' | sort)
  [[ "${#JSON_FILES[@]}" -gt 0 ]] || die "No .json files found in $JSON_PATH"

  TOTAL=${#JSON_FILES[@]}
  PUSHED=0; SKIPPED=0; FAILED=0; INVALID=0
  echo "Found $TOTAL JSON file(s)."
  echo ""

  for f in "${JSON_FILES[@]}"; do
    if ! is_valid_workitem_json "$f"; then
      echo "⚠ Skipping $f — not a valid work item JSON (missing 'Title')." >&2
      ((++INVALID)) || true
      continue
    fi
    echo "── $f ──"
    if push_one_json "$f" "$WORK_ITEM_TYPE"; then
      ((++PUSHED)) || true
    else
      rc=$?
      if [[ $rc -eq 2 ]]; then ((++SKIPPED)) || true; else ((++FAILED)) || true; fi
    fi
    echo ""
  done

  echo "════════════════════════════════════"
  echo "Summary: $PUSHED pushed | $SKIPPED skipped (already tracked) | $FAILED failed | $INVALID invalid"
  echo "Tracker: $TRACKER_FILE"
else
  push_one_json "$JSON_PATH" "$WORK_ITEM_TYPE" || {
    rc=$?
    [[ $rc -eq 2 ]] && exit 0
    exit "$rc"
  }
fi
