import { el } from './dom';
import { saveSettings, settings, type Settings } from './settings';

type Toggle = { [K in keyof Settings]: Settings[K] extends boolean ? K : never }[keyof Settings];

const LABELS: [Toggle, string][] = [
  ['edgePan', "Déplacer la vue quand le pointeur touche un bord de l'écran"],
  ['alwaysBars', 'Toujours afficher les barres de coque et de bouclier'],
  ['autoAbilities', 'Mes vaisseaux utilisent leurs capacités seuls quand c\'est utile'],
  ['muted', 'Couper le son (touche M en bataille)'],
];

/** The options dialog content; changes apply and are saved right away. */
export function optionsPanel(onClose: () => void): HTMLElement {
  const p = el('div', { class: 'panel' },
    '<h2>Options</h2>' +
    LABELS.map(([key, label]) => `<label class="check"><input type="checkbox" data-setting="${key}"> ${label}</label>`).join('') +
    `<label class="range">Volume des effets<input type="range" name="volume" min="0" max="100" step="5" value="${Math.round(settings.volume * 100)}"></label>` +
    '<div class="actions"><button class="primary">Fermer</button></div>');
  for (const input of p.querySelectorAll<HTMLInputElement>('[data-setting]')) {
    const key = input.dataset.setting as Toggle;
    input.checked = settings[key];
    input.onchange = () => {
      settings[key] = input.checked;
      saveSettings();
    };
  }
  const volume = p.querySelector<HTMLInputElement>('[name=volume]')!;
  volume.oninput = () => {
    settings.volume = Number(volume.value) / 100;
    saveSettings();
  };
  p.querySelector('button')!.onclick = onClose;
  return p;
}
