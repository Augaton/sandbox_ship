/**
 * Wraps a Phaser keyboard listener so that it sees each event once. Phaser replays its whole
 * key queue on every new key event of the same frame: without this, a quick tap (down and up
 * within one frame) would act twice, and Space would pause then resume at once.
 */
export function onceEach<T extends Event>(fn: (ev: T) => void): (ev: T) => void {
  const seen = new WeakSet<Event>();
  return (ev) => {
    if (seen.has(ev)) return;
    seen.add(ev);
    fn(ev);
  };
}
