// Builds the finished version into release/, which `npm run play` serves. It only runs on a
// clean main branch, so that the playable version never picks up work in progress from dev.
import { execSync } from 'node:child_process';

const git = (args) => execSync(`git ${args}`).toString().trim();
const run = (cmd) => execSync(cmd, { stdio: 'inherit' });

const branch = git('rev-parse --abbrev-ref HEAD');
if (branch !== 'main' || git('status --porcelain') !== '') {
  console.error(`La version jouable se construit depuis main, sans modification en cours (branche actuelle : ${branch}).`);
  console.error('Pour publier : git switch main && git merge dev && npm run release');
  process.exit(1);
}
run('npx vitest run');
run('npx tsc --noEmit');
run('npx vite build --outDir release --emptyOutDir');
console.log('Version publiée dans release/. Lance-la avec npm run play.');
