Single-line text field with optional label, leading icon, hint, and error state.

```jsx
<Input label="Workspace" placeholder="acme-co" icon="search" hint="Lowercase, no spaces." />
<Input label="Email" type="email" error="That doesn't look right." required />
```

Forwards all native `<input>` props. Pass `error` to show the invalid state (replaces `hint`).
