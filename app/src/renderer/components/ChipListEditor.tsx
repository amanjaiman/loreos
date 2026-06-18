import { useState, type KeyboardEvent } from 'react';

import { Button, Input, Tag } from '../design-system';

/**
 * Edits a list of short strings as removable chips with an add field — the shape the
 * blocklist (apps, keywords) uses. Reused by onboarding (T004) and Settings (T010).
 * Controlled: the parent owns the list.
 */
export function ChipListEditor({
  label,
  hint,
  placeholder,
  items,
  onChange,
}: {
  label: string;
  hint?: string;
  placeholder?: string;
  items: string[];
  onChange: (items: string[]) => void;
}): JSX.Element {
  const [draft, setDraft] = useState('');

  const add = (): void => {
    const value = draft.trim();
    if (
      value.length > 0 &&
      !items.some((i) => i.toLowerCase() === value.toLowerCase())
    ) {
      onChange([...items, value]);
    }
    setDraft('');
  };

  const remove = (item: string): void => {
    onChange(items.filter((i) => i !== item));
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'Enter') {
      event.preventDefault();
      add();
    }
  };

  return (
    <div className="chip-editor">
      <div className="chip-editor__row">
        <Input
          label={label}
          hint={hint}
          placeholder={placeholder}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={onKeyDown}
        />
        <Button
          variant="secondary"
          icon="plus"
          onClick={add}
          disabled={draft.trim().length === 0}
        >
          Add
        </Button>
      </div>
      {items.length > 0 && (
        <div className="chip-editor__chips">
          {items.map((item) => (
            <Tag key={item} onRemove={() => remove(item)}>
              {item}
            </Tag>
          ))}
        </div>
      )}
    </div>
  );
}
