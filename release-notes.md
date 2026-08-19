## What's new

- **Tune what Lore captures, in plain English.** Settings → Capture & Privacy now has three
  controls: how closely Lore watches, how sure it has to be before keeping a memory, and how
  much detail each memory carries. Each one tells you what your current setting actually does,
  and every underlying value stays editable in `config.json` — where anything you set by hand
  wins over the preset.
- **Lore no longer keeps evidence forever.** The episodes and decision trail behind your
  memories are now pruned after 90 days by default (`capture.retentionDays`; set `0` to keep
  them). Your memories themselves are never touched by this.
- **Lore remembers things from more than six months ago again.** Recall was quietly dropping
  older experiences: a booking from last year could not surface no matter how well it matched.
  Relevance and confidence are now judged separately, so an old memory stays findable and
  simply ranks below newer ones. If you have been using Lore for a while, expect it to bring
  up things it had stopped mentioning.
- **Agents can look across your history.** Ask a connected agent to book a flight and it can
  pull your last several bookings rather than the single closest match, then work out the
  pattern itself.
- **Windows that rewrite their titles are captured again.** Media players, terminals printing
  progress, and chat apps with unread counters were silently invisible to Lore — they never
  stayed still long enough to qualify.
- **Better evidence behind each memory.** Lore now weighs where you actually spent your time
  when deciding what to remember from a stretch of work, instead of treating a fifteen-minute
  read and a thirty-second glance as equally representative.
- **Troubleshooting switch.** *Record what Lore reads* (off by default) captures what Lore saw,
  bounded to the last 24 hours or 500 rows, for when you want to know why an app isn't being
  picked up. Turning it off deletes what it collected.

## Note on defaults

Lore now re-reads an unchanged window every **25 seconds** instead of 30, and allows **48**
observations per episode instead of 40. Episode length and the number of AI calls Lore makes
are unchanged — this reads your screen about 20% more often, which costs a little more CPU.
Move *How closely Lore watches* to **Light** if you would rather it did less.
