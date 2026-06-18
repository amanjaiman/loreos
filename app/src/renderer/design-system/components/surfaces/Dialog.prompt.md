Centered modal. Controlled by `open`; closes on overlay click, ×, or Escape via `onClose`.

```jsx
<Dialog open={open} onClose={() => setOpen(false)} title="Forget this memory?"
  footer={<>
    <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
    <Button variant="danger">Forget</Button>
  </>}>
  Lore will stop surfacing this thread. This can't be undone.
</Dialog>
```
