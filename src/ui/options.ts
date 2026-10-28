import { el } from './dom';
import { saveSettings, settings, type Settings } from './settings';

const LABELS: [keyof Settings, string][] = [
  ['edgePan', "Déplacer la vue quand le pointeur touche un bord de l'écran"],
  ['alwaysBars', 'Toujours afficher les barres de coque et de bouclier'],
];

/** The options dialog content; changes apply and are saved right away. */
export function optionsPanel(onClose: () => void): HTMLElement {
  const p = el('div', { class: 'panel' },
    '<h2>Options</h2>' +
    LABELS.map(([key, label]) => `<label class="check"><input type="checkbox" data-setting="${key}"> ${label}</label>`).join('') +
    '<div class="actions"><button class="primary">Fermer</button></div>');
  for (const input of p.querySelectorAll<HTMLInputElement>('[data-setting]')) {
    const key = input.dataset.setting as keyof Settings;
    input.checked = settings[key];
    input.onchange = () => {
      settings[key] = input.checked;
      saveSettings();
    };
  }
  p.querySelector('button')!.onclick = onClose;
  return p;
}
