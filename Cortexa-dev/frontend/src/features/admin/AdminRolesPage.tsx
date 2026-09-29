import { useEffect, useState } from 'react';
import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader, SectionCard } from '../../shared/layout/PageHeader';
import { Button, Skeleton, ErrorState } from '../../shared/ds';
import { useToast } from '../../shared/ds/Toast';
import type { Role } from '../../core/auth/authTypes';
import {
  fetchPermissions,
  fetchRolePermissions,
  replaceRolePermissions,
  type PermissionDto,
} from './adminRolesRepository';

const ROLES: Role[] = ['Researcher', 'Reviewer', 'Admin', 'SuperAdmin'];

type Matrix = Record<Role, Set<string>>;

function emptyMatrix(): Matrix {
  return { Researcher: new Set(), Reviewer: new Set(), Admin: new Set(), SuperAdmin: new Set() };
}

export function AdminRolesPage() {
  const { show } = useToast();
  const [permissions, setPermissions] = useState<PermissionDto[]>([]);
  const [matrix, setMatrix] = useState<Matrix>(emptyMatrix());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savingRole, setSavingRole] = useState<Role | null>(null);
  const [dirty, setDirty] = useState<Set<Role>>(new Set());

  async function load() {
    setLoading(true);
    setError(null);
    const perms = await fetchPermissions();
    if (!perms.ok) {
      setError(perms.error.message);
      setLoading(false);
      return;
    }
    setPermissions(perms.data);
    const next = emptyMatrix();
    const results = await Promise.all(ROLES.map((r) => fetchRolePermissions(r)));
    results.forEach((res, i) => {
      const role = ROLES[i]!;
      if (res.ok) next[role] = new Set(res.data.permissions.map((p) => p.name));
    });
    setMatrix(next);
    setDirty(new Set());
    setLoading(false);
  }

  useEffect(() => {
    void load();
  }, []);

  function toggle(role: Role, perm: string) {
    if (role === 'SuperAdmin') return; // SuperAdmin implicitly has everything
    setMatrix((prev) => {
      const set = new Set(prev[role]);
      if (set.has(perm)) set.delete(perm);
      else set.add(perm);
      return { ...prev, [role]: set };
    });
    setDirty((prev) => new Set(prev).add(role));
  }

  async function saveRole(role: Role) {
    setSavingRole(role);
    const result = await replaceRolePermissions(role, Array.from(matrix[role]));
    setSavingRole(null);
    if (result.ok) {
      show({ variant: 'success', title: 'Permissions updated', message: `${role} permission set saved.` });
      setDirty((prev) => {
        const next = new Set(prev);
        next.delete(role);
        return next;
      });
    } else {
      show({ variant: 'error', title: 'Update failed', message: result.error.message });
    }
  }

  return (
    <AppShell>
      <PageHeader title="Roles & Permissions" subtitle="Assign which permissions each role carries. SuperAdmin always has full access." />

      <SectionCard>
        {loading ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {Array.from({ length: 8 }).map((_, i) => (
              <Skeleton key={i} height={40} />
            ))}
          </div>
        ) : error ? (
          <ErrorState title="Couldn't load permissions" message={error} onRetry={() => void load()} />
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: '2.2fr repeat(4,0.8fr)',
                gap: 10,
                paddingBottom: 12,
                borderBottom: '1px solid var(--border-subtle)',
                fontSize: 12,
                fontWeight: 600,
                color: 'var(--text-muted)',
                minWidth: 680,
              }}
            >
              <div>Permission</div>
              {ROLES.map((r) => (
                <div key={r} style={{ textAlign: 'center' }}>
                  {r}
                </div>
              ))}
            </div>

            {permissions.map((p) => (
              <div
                key={p.id ?? p.name}
                style={{
                  display: 'grid',
                  gridTemplateColumns: '2.2fr repeat(4,0.8fr)',
                  gap: 10,
                  alignItems: 'center',
                  padding: '12px 0',
                  borderBottom: '1px solid var(--border-subtle)',
                  minWidth: 680,
                }}
              >
                <div>
                  <div style={{ fontFamily: 'var(--font-mono)', fontSize: 12.5, fontWeight: 600, color: 'var(--text-primary)' }}>{p.name}</div>
                  <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>{p.description}</div>
                </div>
                {ROLES.map((role) => {
                  const checked = role === 'SuperAdmin' || matrix[role].has(p.name);
                  const locked = role === 'SuperAdmin';
                  return (
                    <div key={role} style={{ display: 'flex', justifyContent: 'center' }}>
                      <input
                        type="checkbox"
                        checked={checked}
                        disabled={locked}
                        onChange={() => toggle(role, p.name)}
                        aria-label={`${p.name} for ${role}`}
                        style={{ width: 16, height: 16, cursor: locked ? 'not-allowed' : 'pointer', accentColor: 'var(--accent-primary)' }}
                      />
                    </div>
                  );
                })}
              </div>
            ))}

            <div style={{ display: 'flex', gap: 10, marginTop: 20, flexWrap: 'wrap' }}>
              {ROLES.filter((r) => r !== 'SuperAdmin').map((role) => (
                <Button
                  key={role}
                  variant={dirty.has(role) ? 'primary' : 'secondary'}
                  size="sm"
                  disabled={!dirty.has(role) || savingRole === role}
                  onClick={() => void saveRole(role)}
                >
                  {savingRole === role ? 'Saving…' : `Save ${role}`}
                </Button>
              ))}
            </div>
          </div>
        )}
      </SectionCard>
    </AppShell>
  );
}
