import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader } from '../../shared/layout/PageHeader';
import { SettingsShell } from './SettingsShell';

export function SettingsPage() {
  return (
    <AppShell>
      <PageHeader title="Settings" />
      <SettingsShell />
    </AppShell>
  );
}
