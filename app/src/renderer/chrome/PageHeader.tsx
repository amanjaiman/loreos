import { type ReactNode } from 'react';

export function PageHeader({
  eyebrow,
  title,
  description,
  action,
}: {
  eyebrow: string;
  title: string;
  description: string;
  action?: ReactNode;
}): JSX.Element {
  return (
    <header className="page-header">
      <div className="page-header__copy">
        <span className="page-header__eyebrow">{eyebrow}</span>
        <h1 className="page-header__title">{title}</h1>
        <p className="page-header__description">{description}</p>
      </div>
      {action !== undefined && (
        <div className="page-header__action">{action}</div>
      )}
    </header>
  );
}
