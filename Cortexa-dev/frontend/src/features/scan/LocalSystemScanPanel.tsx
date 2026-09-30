import type { ReactNode } from 'react';
import { Button } from '../../shared/ds';
import { LocalSystemConnectForm } from './LocalSystemConnectForm';
import { promptFor } from './localSystemScanPrompts';
import { RemoteFileBrowser } from './RemoteFileBrowser';
import { ScanLoader } from './ScanLoader';
import { StepPrompt } from './StepPrompt';
import { useLocalSystemScan } from './useLocalSystemScan';

// Covers the form while connecting instead of replacing it, so the entered
// values survive a failed attempt. The loader sticks to the top of the view
// because the form can be taller than the screen.
function ConnectingOverlay({ active, children }: { active: boolean; children: ReactNode }) {
  return (
    <div style={{ position: 'relative' }}>
      {children}
      {active && (
        <div
          style={{
            position: 'absolute',
            inset: 0,
            zIndex: 1,
            borderRadius: 'var(--radius-lg)',
            background: 'color-mix(in srgb, var(--surface-page) 70%, transparent)',
            backdropFilter: 'blur(2px)',
          }}
        >
          <div style={{ position: 'sticky', top: 24, maxWidth: 520, margin: '40px auto 0', background: 'var(--surface-card-solid)', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-hover)' }}>
            <ScanLoader
              title="Connecting to the System"
              message="Opening an SSH session with your key and reading the folder."
              slowHint="This is taking a while. Check that the VM is running and reachable from this network."
            />
          </div>
        </div>
      )}
    </div>
  );
}

export function LocalSystemScanPanel() {
  const scan = useLocalSystemScan();
  const { state } = scan;
  const connected = state.step === 'files';
  const connecting = state.connection.status === 'loading';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 16 }}>
        {connected && (
          <Button size="sm" variant="ghost" onClick={scan.disconnect}>
            Disconnect
          </Button>
        )}
      </div>
      <StepPrompt copy={promptFor(state)} />
      {state.step === 'connect' && (
        <ConnectingOverlay active={connecting}>
          <LocalSystemConnectForm
            connecting={connecting}
            error={state.connection.status === 'error' ? state.connection.error : undefined}
            onConnect={(credentials, path) => void scan.connect(credentials, path)}
          />
        </ConnectingOverlay>
      )}
      {state.step === 'files' && (
        <RemoteFileBrowser
          listing={state.listing}
          currentPath={state.path}
          onNavigate={(path) => void scan.navigate(path)}
          onUp={scan.goUp}
          onRetry={scan.retry}
        />
      )}
    </div>
  );
}
