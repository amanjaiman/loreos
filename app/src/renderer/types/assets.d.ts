// Ambient declarations for non-code imports webpack handles (style-loader/css-loader
// and asset modules). TypeScript needs these so side-effect CSS imports and asset
// URLs typecheck under `tsc --noEmit`.

declare module '*.css';

declare module '*.svg' {
  const url: string;
  export default url;
}
