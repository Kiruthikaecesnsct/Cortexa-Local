import { useState } from 'react';
import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader, SectionCard } from '../../shared/layout/PageHeader';
import { Button, Input, Select, Switch, Modal, Skeleton, ErrorState, PermissionDenied } from '../../shared/ds';
import { useToast } from '../../shared/ds/Toast';
import { useSession } from '../../core/auth/useSession';
import { useOrgUsers } from './useOrgUsers';
import { useUserMutations } from './useUserMutations';
import { createUser } from './adminUsersRepository';
import { ROLE_OPTIONS } from './adminUserTypes';
import type { Role } from '../../core/auth/authTypes';

export function AdminUsersPage() {
  const { show } = useToast();
  const { hasPermission } = useSession();
  const canWrite = hasPermission('admin:users:write');
  const { data, isLoading, error, isForbidden, refetch } = useOrgUsers(true);
  const mutations = useUserMutations({ refetch, onToast: show });

  const [createOpen, setCreateOpen] = useState(false);
  const [form, setForm] = useState({ email: '', username: '', password: '', role: 'Researcher' as Role });
  const [creating, setCreating] = useState(false);

  async function submitCreate() {
    setCreating(true);
    const result = await createUser(form);
    setCreating(false);
    if (result.ok) {
      show({ variant: 'success', title: 'User created', message: `${result.data.email} added.` });
      setCreateOpen(false);
      setForm({ email: '', username: '', password: '', role: 'Researcher' });
      await refetch();
    } else {
      show({ variant: 'error', title: 'Could not create user', message: result.error.message });
    }
  }

  if (isForbidden) {
    return (
      <AppShell>
        <PageHeader title="Users" />
        <PermissionDenied />
      </AppShell>
    );
  }

  return (
    <AppShell>
      <PageHeader
        title="Users"
        actions={
          canWrite ? (
            <Button variant="primary" onClick={() => setCreateOpen(true)}>
              + Create User
            </Button>
          ) : undefined
        }
      />

      <SectionCard>
        {isLoading && !data ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} height={44} />
            ))}
          </div>
        ) : error ? (
          <ErrorState title="Couldn't load users" message={error.message} correlationId={error.correlationId} onRetry={() => void refetch()} />
        ) : (
          <>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: '2fr 1fr 1fr 1fr',
                gap: 12,
                paddingBottom: 12,
                borderBottom: '1px solid var(--border-subtle)',
                fontSize: 12,
                fontWeight: 600,
                color: 'var(--text-muted)',
              }}
            >
              <div>Email</div>
              <div>Username</div>
              <div>Role</div>
              <div>Status</div>
            </div>
            {(data ?? []).map((u) => (
              <div
                key={u.id}
                style={{
                  display: 'grid',
                  gridTemplateColumns: '2fr 1fr 1fr 1fr',
                  gap: 12,
                  alignItems: 'center',
                  padding: '14px 0',
                  borderBottom: '1px solid var(--border-subtle)',
                  fontSize: 13.5,
                }}
              >
                <div style={{ color: 'var(--text-primary)', fontWeight: 500 }}>{u.email}</div>
                <div style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>{u.username}</div>
                <Select
                  options={ROLE_OPTIONS}
                  value={u.role}
                  placeholder=""
                  disabled={!canWrite || mutations.isRunning}
                  onChange={(e) => void mutations.run({ kind: 'changeRole', userId: u.id, username: u.username, newRole: e.target.value as Role })}
                />
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <Switch
                    checked={u.isEnabled}
                    disabled={!canWrite || mutations.isRunning}
                    onChange={(v) => void mutations.run({ kind: v ? 'enable' : 'disable', userId: u.id, username: u.username })}
                  />
                  <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>{u.isEnabled ? 'Enabled' : 'Disabled'}</span>
                </div>
              </div>
            ))}
          </>
        )}
      </SectionCard>

      <Modal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        title="Create User"
        subtitle="Add a new member to your organization."
        footer={
          <>
            <Button variant="secondary" fullWidth onClick={() => setCreateOpen(false)}>
              Cancel
            </Button>
            <Button fullWidth disabled={creating || !form.email || !form.username || !form.password} onClick={() => void submitCreate()}>
              {creating ? 'Creating…' : 'Create'}
            </Button>
          </>
        }
      >
        <Input label="Email" type="email" placeholder="user@acme.com" value={form.email} onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))} />
        <Input label="Username" placeholder="jchen" value={form.username} onChange={(e) => setForm((f) => ({ ...f, username: e.target.value }))} />
        <Input label="Temporary Password" type="password" placeholder="••••••••" value={form.password} onChange={(e) => setForm((f) => ({ ...f, password: e.target.value }))} />
        <Select label="Role" options={ROLE_OPTIONS} value={form.role} placeholder="" onChange={(e) => setForm((f) => ({ ...f, role: e.target.value as Role }))} />
      </Modal>
    </AppShell>
  );
}
