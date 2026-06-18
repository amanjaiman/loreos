import { useState } from 'react';

import { IconButton } from '../design-system';

/**
 * A copy-paste code/config block with a copy affordance. Used by Connect to surface the
 * MCP config snippets and CLI installer commands. Clipboard only — no network.
 */
export function CodeBlock({
  code,
  label,
}: {
  code: string;
  label?: string;
}): JSX.Element {
  const [copied, setCopied] = useState(false);

  const copy = (): void => {
    void navigator.clipboard.writeText(code).then(() => {
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    });
  };

  return (
    <div className="codeblock">
      {label !== undefined && <div className="codeblock__label">{label}</div>}
      <div className="codeblock__surface">
        <pre className="codeblock__pre">
          <code>{code}</code>
        </pre>
        <IconButton
          icon={copied ? 'check' : 'copy'}
          label={copied ? 'Copied' : 'Copy'}
          onClick={copy}
          className="codeblock__copy"
        />
      </div>
    </div>
  );
}
