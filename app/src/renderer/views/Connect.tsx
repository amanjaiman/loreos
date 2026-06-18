import { useState } from 'react';

import { Badge, Card, Tabs } from '../design-system';
import { CodeBlock } from '../components/CodeBlock';
import { useSystemStatus } from '../lib/hooks';
import './connect.css';

type ClientTab = 'claude-desktop' | 'claude-code' | 'cursor' | 'cli';

const EXE = 'C:\\\\Program Files\\\\Lore\\\\LoreAgent.exe';

const STDIO_JSON = `{
  "mcpServers": {
    "lore": {
      "command": "${EXE}",
      "args": ["--mcp"]
    }
  }
}`;

const CURSOR_HTTP_JSON = `{
  "mcpServers": {
    "lore": {
      "url": "http://127.0.0.1:7842/mcp"
    }
  }
}`;

const TABS = [
  { value: 'claude-desktop', label: 'Claude Desktop' },
  { value: 'claude-code', label: 'Claude Code' },
  { value: 'cursor', label: 'Cursor' },
  { value: 'cli', label: 'CLI & skill' },
];

/**
 * Connect — wire up MCP clients. Copy-paste config blocks consistent with the 006
 * integration docs, plus the 007 one-line installers, and a live hint of whether Lore is
 * running (so the client will be able to connect). All read-only via api.ts.
 */
export function Connect(): JSX.Element {
  const [tab, setTab] = useState<ClientTab>('claude-desktop');
  const { offline } = useSystemStatus();

  return (
    <div className="app-page">
      <div className="connect-status">
        <Badge variant={offline ? 'warning' : 'success'} dot>
          {offline ? "Lore isn't running" : 'Lore is running'}
        </Badge>
        <span className="connect-status__line">
          {offline
            ? 'Start Lore so your tools can reach it on 127.0.0.1:7842.'
            : 'Ready to connect — your tools call your memory on this machine only.'}
        </span>
      </div>

      <Tabs tabs={TABS} value={tab} onChange={(v) => setTab(v as ClientTab)} />

      {tab === 'claude-desktop' && (
        <Card eyebrow="// mcp · stdio" title="Claude Desktop">
          <p className="connect-step">
            Edit{' '}
            <span className="connect-mono">
              %APPDATA%\Claude\claude_desktop_config.json
            </span>{' '}
            (Settings → Developer → Edit Config), add the{' '}
            <span className="connect-mono">lore</span> server, then fully
            restart Claude Desktop.
          </p>
          <CodeBlock code={STDIO_JSON} />
          <p className="connect-tip">
            Or let the CLI do it:{' '}
            <span className="connect-mono">
              lore mcp install claude-desktop
            </span>{' '}
            — it merges, never overwrites, and backs up first.
          </p>
        </Card>
      )}

      {tab === 'claude-code' && (
        <Card eyebrow="// mcp · stdio or http" title="Claude Code">
          <p className="connect-step">Add Lore from your terminal:</p>
          <CodeBlock
            code={`claude mcp add lore -- "C:\\Program Files\\Lore\\LoreAgent.exe" --mcp`}
          />
          <p className="connect-tip">
            Add <span className="connect-mono">--scope user</span> to make Lore
            available in every project. Prefer HTTP, with the app already
            running?
          </p>
          <CodeBlock
            code={`claude mcp add --transport http lore http://127.0.0.1:7842/mcp`}
          />
          <p className="connect-tip">
            Verify with <span className="connect-mono">claude mcp list</span> —
            lore should be connected.
          </p>
        </Card>
      )}

      {tab === 'cursor' && (
        <Card eyebrow="// mcp · stdio or http" title="Cursor">
          <p className="connect-step">
            Edit{' '}
            <span className="connect-mono">%USERPROFILE%\.cursor\mcp.json</span>{' '}
            (or Settings → MCP → Add new global MCP server) and add the{' '}
            <span className="connect-mono">lore</span> server.
          </p>
          <CodeBlock code={STDIO_JSON} label="stdio — Cursor launches Lore" />
          <CodeBlock
            code={CURSOR_HTTP_JSON}
            label="Streamable HTTP — Lore app already running"
          />
          <p className="connect-tip">
            Or run <span className="connect-mono">lore mcp install cursor</span>
            .
          </p>
        </Card>
      )}

      {tab === 'cli' && (
        <Card eyebrow="// terminal & agents" title="CLI and the Lore skill">
          <p className="connect-step">
            The <span className="connect-mono">lore</span> CLI wires Lore into
            your tools for you — each installer merges, never overwrites, and
            backs up first:
          </p>
          <CodeBlock
            code={`lore mcp install claude-desktop\nlore mcp install cursor\nlore mcp install claude-code`}
          />
          <p className="connect-tip">
            Teach a skill-capable agent (Claude Code) to use your memory
            automatically:
          </p>
          <CodeBlock code={`lore skills install claude-code`} />
        </Card>
      )}
    </div>
  );
}
