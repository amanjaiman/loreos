The default content surface. Compose with eyebrow/title/subtitle/footer slots or drop arbitrary children in the body.

```jsx
<Card eyebrow="Memory" title="Morning sync" subtitle="3 sources · updated 2m ago"
      footer={<Button size="sm" variant="outline">Open</Button>}>
  Lore summarized your standup and linked the related thread.
</Card>
```

Set `interactive` for clickable cards (hover lift).
