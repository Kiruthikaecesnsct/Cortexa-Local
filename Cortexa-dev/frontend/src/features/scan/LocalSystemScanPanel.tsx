import { Button } from '../../shared/ds';
import { LocalSystemConnectForm } from './LocalSystemConnectForm';
import { promptFor } from './localSystemScanPrompts';
import { RemoteFileBrowser } from './RemoteFileBrowser';
import { StepPrompt } from './StepPrompt';
import { useLocalSystemScan } from './useLocalSystemScan';

export function LocalSystemScanPanel() {
  const scan = useLocalSystemScan();
  const { state } = scan;
  const connected = state.step === 'files';

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
        <LocalSystemConnectForm
          connecting={state.connection.status === 'loading'}
          error={state.connection.status === 'error' ? state.connection.error : undefined}
          onConnect={(credentials, path) => void scan.connect(credentials, path)}
        />
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
