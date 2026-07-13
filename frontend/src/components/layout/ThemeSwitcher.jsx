import { useTranslation } from 'react-i18next';
import { THEMES } from '../../theme';
import styles from './ThemeSwitcher.module.css';

// دوائر الألوان تعكس بترولي/زعفراني كل سمة حرفياً — بلا منطق حساب ألوان في
// JS، فقط ثابت عرض يطابق تعريفات [data-theme] في styles.css.
const SWATCH_COLORS = {
  default: '#0F3B3A',
  ocean: '#0A2540',
  rose: '#3D1A24',
  forest: '#1B3A2D',
};

export default function ThemeSwitcher({ current, onChange }) {
  const { t } = useTranslation();

  return (
    <div className={styles.switcher} role="group" aria-label={t('nav.themeSwitcherAria')}>
      {THEMES.map((theme) => (
        <button
          key={theme}
          type="button"
          className={`${styles.swatch} ${current === theme ? styles.active : ''}`}
          style={{ background: SWATCH_COLORS[theme] }}
          onClick={() => onChange(theme)}
          aria-pressed={current === theme}
          aria-label={t(`nav.theme.${theme}`)}
          title={t(`nav.theme.${theme}`)}
        />
      ))}
    </div>
  );
}
