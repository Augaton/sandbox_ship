// `npm run balance`: AI against AI, many times, to measure the factions. Slow, so kept out of `npm test`.
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: { include: ['tests/balance/*.ts'], testTimeout: 0, silent: false },
});
