# Cortexa — Security Model

This document covers the identity and RBAC security model for Cortexa. It is a reference for developers working on the identity service, the gateway, or any backend service that consumes identity claims.

---

## 1. Role hierarchy

Four roles, ascending privilege: Researcher, Reviewer, Admin, SuperAdmin.

SuperAdmin is a platform-level account. It is seeded at identity service startup with `is_system = true`. No API endpoint and no Entra group mapping can assign or create a SuperAdmin account. The only mutation path for this account is a direct database write reviewed as a deliberate migration.

Roles are enforced at two independent layers: the gateway (permission claim check per route) and the identity service (handler-level org scope). Both must pass. Bypassing one does not bypass the other.

---

## 2. Permission model

Permissions are named capabilities, for example `documents:write`. They are stored in the `permissions` table and seeded at identity service startup.

Role-to-permission assignments live in `role_permissions`. A SuperAdmin can edit these at runtime via `PUT /admin/permissions/{role}`. Changes apply to new tokens only — existing tokens keep the permissions that were embedded when they were minted, until those tokens expire.

At login, the identity service queries the current `role_permissions` rows for the user's role and embeds the result as a `perms` JSON array in the JWT. The gateway reads this claim on every request. It does not call back to the identity service to re-check permissions on each request — it trusts the claim in the signed token.

---

## 3. Enforcement points

### Gateway (YARP + RouteRoleHandler)

- Validates JWT signature, issuer, audience, and expiry on every request.
- Calls the identity service's `/internal/users/{id}/status` endpoint to check `UserStatus` and security stamp freshness. The check is fail-closed: if the identity service is unreachable, the gateway rejects the request.
- Routes with no RBAC metadata in the YARP config are denied by default, even with a valid token.
- If the JWT `role` claim is `SuperAdmin`, the gateway skips the `perms` check and forwards the request directly.
- Strips `X-Org-Id`, `X-User-Id`, and `X-User-Email` from the incoming request, then re-injects them from the validated JWT claims. Clients cannot set or spoof these headers.

### Identity service (handler layer)

- All admin operations read `org_id` from the JWT claim via `ResolveOrgId` and scope database queries to that value.
- `GetByIdInOrgAsync` enforces the cross-org boundary: it returns null if the target user's `org_id` does not match the caller's `org_id`, which the handler converts to a 403.
- `ChangeOrgUserRoleHandler` blocks assigning the `SuperAdmin` role — it returns 400 before touching the database.
- Last-Admin protection: demoting or disabling an Admin returns 400 if they are the only active Admin in the organization. The check uses a transaction-scoped count query.
- Any mutation path that touches a user calls `EnsureMutable()` on the `User` aggregate. If `is_system` is true, this throws, which propagates to a 400 or 409 depending on the handler.

---

## 4. Multi-tenancy design

`users.org_id` is a non-nullable FK to `organizations` for every user except the SuperAdmin account (which carries `org_id = null` permanently — same `EnsureMutable` guard prevents assignment).

`organizations.deleted_at` is a soft-delete flag. The `users.org_id` FK is `ON DELETE RESTRICT`. Hard-deleting an organization row while any user still references it fails at the database level. This is intentional: it forces an explicit decision (reassign users or leave the org soft-deleted) and prevents accidental mass account loss.

The `org_id` JWT claim is set from `User.OrganizationId` at mint time. SuperAdmin tokens omit the claim entirely rather than emitting an empty string. Backend services must treat a missing `X-Org-Id` header as "caller is SuperAdmin" and apply cross-org access where the service design requires it. A present but empty header is not a valid substitute.

---

## 5. Auth flows

**Password login** (`POST /auth/login`): validate credentials → check lockout → check disabled status → issue access token + refresh token.

**Entra ID login** (`POST /auth/entra`): validate the Entra-issued token → apply group mapping → upsert the user record → issue tokens.

**Refresh** (`POST /auth/refresh`): validate the refresh token → check the security stamp → issue a new access token and refresh token.

**Logout** (`POST /auth/logout`): revoke the refresh token (best-effort), expire the cookie.

Both login paths go through the same disabled-user and lockout checks before issuing any token.

---

## 6. Account protection features

**Failed-login lockout.** The identity service tracks failed attempts per user in `failed_login_attempts`. Policy is config-driven (`Lockout` section in `appsettings.json`). Lockout duration doubles on each recurrence up to `MaxLockoutSeconds`. While locked, `/auth/login` returns `429 Too Many Requests` with a `Retry-After` header. A successful login resets the counter.

**Disabled-user enforcement.** Disabling a user rotates their `security_stamp` and immediately revokes all active refresh tokens. `/auth/login` and `/auth/refresh` both reject a disabled user with 403 before issuing or renewing any token.

**Security stamp.** The stamp is rotated on role change, permission change, and account disable. The gateway checks stamp freshness on every request via the `/internal/users/{id}/status` endpoint. A stale stamp is treated as an invalid session.

**Immutable SuperAdmin.** `is_system = true` blocks every mutation path in application code. There is no recovery API — password reset requires a direct, reviewed database write.

---

## 7. Attack surface

| Threat | Control |
| ------ | ------- |
| Token forgery | HS256 signing with a shared secret stored in Key Vault. Signature is validated by the gateway on every request. |
| Stale token after disable or role change | Security stamp checked against the identity service on every request. Fail-closed: gateway rejects requests when the identity service is unreachable. |
| Cross-org data access | `org_id` injected by the gateway from the JWT claim. Backend services scope queries to `org_id`. `GetByIdInOrgAsync` enforces the boundary at the handler layer. |
| Privilege escalation via API | SuperAdmin role blocked in create and role-change handlers (400). Gateway requires explicit `perms` for all admin routes. |
| Header spoofing (`X-Org-Id`, `X-User-Id`, `X-User-Email`) | Gateway strips these headers from incoming requests and re-injects from validated JWT claims. |
| Brute-force login | Exponential lockout at the identity service. Rate limiting at the gateway layer. |
| Last-admin lockout | Domain guard in disable and role-change handlers: blocks the operation if it would leave an org with no active Admin. |
| Replay of a revoked refresh token | Refresh tokens are stored hashed. Revoked on logout, account disable, and stamp rotation. |

---

## 8. Known limitations

- Permission changes via `PUT /admin/permissions/{role}` take effect at next login, not immediately. A user with a valid in-flight token retains their old permissions until that token expires.
- Refresh token lifetime is 7 days. Stamp-based revocation is the primary real-time invalidation path for issued tokens — there is no active push revocation list.
- No IP-based allow/deny list for API access beyond the gateway-level CORS policy.
- Entra group sync is pull-based at login time. If a user's group memberships change in Entra, those changes do not affect their existing Cortexa session until they re-authenticate.
