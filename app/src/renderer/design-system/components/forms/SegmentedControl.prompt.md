A closed set of mutually exclusive stops, all visible at once — a radio group wearing a
segmented shell. Use it for two to four stops where the choice is a preference, not a
number; reach for Select once the list grows. The `hint` is the honest place for a live
sentence describing what the current stop actually does.

```jsx
<SegmentedControl
  label="How closely Lore watches"
  options={[
    { value: 'light', label: 'Light' },
    { value: 'balanced', label: 'Balanced' },
    { value: 'close', label: 'Close' },
  ]}
  value={attentiveness}
  onChange={setAttentiveness}
  hint="Lore reads your screen about every 25 seconds."
/>

<SegmentedControl options={['Day', 'Week', 'Month']} defaultValue="Week" />
```
