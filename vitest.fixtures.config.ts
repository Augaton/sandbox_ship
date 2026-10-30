// `npm run fixtures`: reference results from this version, for the Godot port's tests.
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: { include: ['tests/fixtures/*.ts'], testTimeout: 0 },
});
