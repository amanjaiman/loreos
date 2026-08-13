import { useState } from 'react';

import { CodeBlock } from '../../components/CodeBlock';
import { Badge, Button, Card } from '../../design-system';
import { useSystemStatus } from '../../lib/hooks';
import './connections.css';

type ConnectedItem = {
  client: string;
  surface: string;
  path: string;
  action: string;
};

/** One provider-neutral setup action, with protocol details kept in an advanced card. */
export function Connections(): JSX.Element {
  const { offline } = useSystemStatus();
  const [connecting, setConnecting] = useState(false);
  const [results, setResults] = useState<ConnectedItem[]>([]);
  const [message, setMessage] = useState<string | null>(null);

  const connect = async (): Promise<void> => {
    setConnecting(true);
    setMessage(null);
    try {
      const result = await window.lore.connectAgents();
      setResults(result.connected);
      if (!result.supported) {
        setMessage(
          'Run lore connect in a terminal when using a development build.',
        );
      } else if (result.error !== null) {
        setMessage(result.error);
      } else {
        setMessage(
          'Connected. Restart open agent tools so they discover Lore.',
        );
      }
    } catch {
      setMessage("Couldn't connect agent tools.");
    } finally {
      setConnecting(false);
    }
  };

  return (
    <div className="settings-connections">
      <div className="connect-status">
        <Badge variant={offline ? 'warning' : 'success'} dot>
          {offline ? "Lore isn't running" : 'Lore is running'}
        </Badge>
        <span className="connect-status__line">
          {offline
            ? 'Start Lore before an agent tries to recall your memory.'
            : 'Your memory stays on this machine at 127.0.0.1:7842.'}
        </span>
      </div>

      <Card eyebrow="// recommended" title="Agent tools">
        <p className="connect-step">
          Connect once. Lore installs its portable skill in the shared Agent
          Skills location, adds a Claude Code compatibility copy, and adds the
          MCP shim needed by detected desktop clients.
        </p>
        <Button
          variant="primary"
          onClick={() => void connect()}
          disabled={connecting}
        >
          {connecting ? 'Connecting…' : 'Connect agent tools'}
        </Button>
        <p className="connect-tip">
          Terminal equivalent:{' '}
          <span className="connect-mono">lore connect</span>
        </p>
        {results.length > 0 && (
          <ul className="connect-results">
            {results.map((item) => (
              <li key={`${item.client}-${item.path}`}>
                <span>{item.client}</span>
                <span>
                  {item.surface} · {item.action}
                </span>
              </li>
            ))}
          </ul>
        )}
        {message !== null && <p className="connect-note">{message}</p>}
      </Card>

      <Card eyebrow="// advanced" title="MCP">
        <p className="connect-step">
          MCP remains available for clients that cannot run the Lore CLI, or
          when you want Lore's native tools instead of the ambient skill
          workflow.
        </p>
        <CodeBlock
          code="http://127.0.0.1:7842/mcp"
          label="Streamable HTTP endpoint"
        />
        <p className="connect-tip">
          Existing <span className="connect-mono">lore mcp install …</span>{' '}
          commands remain supported for manual setup.
        </p>
      </Card>
    </div>
  );
}
