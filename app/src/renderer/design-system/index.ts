// The Lore Design System — the single import surface for the vendored bundle
// (namespace `LoreDesignSystem_d94c44`). Components are consumed UNMODIFIED from
// the handoff bundle (constitution: restyle via tokens, never fork a component);
// each is a `.jsx` module (runtime) paired with a `.d.ts` (types). Webpack resolves
// the `.jsx`, TypeScript the `.d.ts`. The bundle's components read `window.React`
// and `window.lucide`, so `lib/ds-globals.ts` must run before any of them render.
//
// Styles live in `./styles.css`, linked once at the renderer entry.

// forms
export { Button } from './components/forms/Button';
export type { ButtonProps } from './components/forms/Button';
export { IconButton } from './components/forms/IconButton';
export type { IconButtonProps } from './components/forms/IconButton';
export { Input } from './components/forms/Input';
export type { InputProps } from './components/forms/Input';
export { Textarea } from './components/forms/Textarea';
export type { TextareaProps } from './components/forms/Textarea';
export { Select } from './components/forms/Select';
export type { SelectProps } from './components/forms/Select';
export { Checkbox } from './components/forms/Checkbox';
export type { CheckboxProps } from './components/forms/Checkbox';
export { Radio } from './components/forms/Radio';
export type { RadioProps } from './components/forms/Radio';
export { Switch } from './components/forms/Switch';
export type { SwitchProps } from './components/forms/Switch';
export { SegmentedControl } from './components/forms/SegmentedControl';
export type {
  SegmentedControlProps,
  SegmentedOption,
} from './components/forms/SegmentedControl';

// feedback
export { Badge } from './components/feedback/Badge';
export type { BadgeProps } from './components/feedback/Badge';
export { Tag } from './components/feedback/Tag';
export type { TagProps } from './components/feedback/Tag';
export { Spinner } from './components/feedback/Spinner';
export type { SpinnerProps } from './components/feedback/Spinner';
export { ProgressBar } from './components/feedback/ProgressBar';
export type { ProgressBarProps } from './components/feedback/ProgressBar';
export { Toast } from './components/feedback/Toast';
export type { ToastProps } from './components/feedback/Toast';
export { Tooltip } from './components/feedback/Tooltip';
export type { TooltipProps } from './components/feedback/Tooltip';

// surfaces
export { Card } from './components/surfaces/Card';
export type { CardProps } from './components/surfaces/Card';
export { Avatar } from './components/surfaces/Avatar';
export type { AvatarProps } from './components/surfaces/Avatar';
export { Tabs } from './components/surfaces/Tabs';
export type { TabsProps } from './components/surfaces/Tabs';
export { Divider } from './components/surfaces/Divider';
export type { DividerProps } from './components/surfaces/Divider';
export { Dialog } from './components/surfaces/Dialog';
export type { DialogProps } from './components/surfaces/Dialog';

// theming
export { ThemeSelector, LORE_THEMES, applyLoreTheme } from './components/theming/ThemeSelector';
export type { ThemeSelectorProps, LoreTheme } from './components/theming/ThemeSelector';
