import { useEffect, useMemo, type ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { SettingsTabList, IconSettings, IconShield, IconUsers, type SettingsTabDef } from '../../shared/ds';
import { ModelConfigTab } from './tabs/ModelConfigTab';
import { PatentApiTab } from './tabs/PatentApiTab';

interface SettingsTabEntry extends SettingsTabDef {
  render?: () => ReactNode;
}

// Extension point: append a { id, label, icon, render } entry here to add a new
// Settings tab. Set status: 'future' for a visible-but-disabled placeholder
// (no render needed) until the tab's content is implemented.
const SETTINGS_TABS: SettingsTabEntry[] = [
  { id: 'model', label: 'Model Configuration', icon: <IconSettings size={16} />, render: () => <ModelConfigTab /> },
  { id: 'patent-api', label: 'Patent API', icon: <IconShield size={16} />, render: () => <PatentApiTab /> },
  { id: 'user-management', label: 'User Management', icon: <IconUsers size={16} />, status: 'future' },
];

const DEFAULT_TAB_ID = SETTINGS_TABS[0]!.id;

function resolveActiveId(tabId: string | undefined): string {
  const match = SETTINGS_TABS.find((t) => t.id === tabId && t.render);
  return match ? match.id : DEFAULT_TAB_ID;
}

export function SettingsShell() {
  const { tabId } = useParams<{ tabId?: string }>();
  const navigate = useNavigate();
  const activeId = resolveActiveId(tabId);

  useEffect(() => {
    if (tabId !== activeId) navigate(`/settings/${activeId}`, { replace: true });
  }, [tabId, activeId, navigate]);

  const activeTab = useMemo(() => SETTINGS_TABS.find((t) => t.id === activeId), [activeId]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      <SettingsTabList tabs={SETTINGS_TABS} activeId={activeId} onChange={(id) => navigate(`/settings/${id}`)} />
      <div
        role="tabpanel"
        id={`panel-${activeId}`}
        aria-labelledby={`tab-${activeId}`}
        tabIndex={0}
        style={{ marginTop: 20 }}
      >
        {activeTab?.render?.()}
      </div>
    </div>
  );
}
