## What's new

- **Every page now updates itself.** Home, Memory, and Activity used to load once and then
  sit there: a memory Lore captured while you were looking at Home wouldn't appear until you
  navigated to another page and came back. All three now refresh on their own — Home and
  Activity every 15 seconds, Memory every minute — so what's on screen is what Lore knows.
- **You can watch a memory arrive.** Because Home stays live, a newly kept memory now lands
  in *Learned today* with its arrival animation while you're looking at it, instead of
  silently showing up on your next visit.
- **The rail and the page no longer disagree.** The staged count in the sidebar refreshed on
  its own while the page underneath it didn't, so the two could show different numbers after
  you kept or dismissed something. They now move together.
- **A dropped reading no longer blanks the page.** If Lore is restarting or briefly
  unreachable, pages keep showing what they last read rather than emptying out.

## Note on resource use

The app only refreshes while its window is actually on screen — minimized to the tray, it
makes no requests at all, and it re-reads immediately when you bring it back. Editing a
memory holds the refresh until you close the card, so a timer can't move a row out from
under you mid-edit.
