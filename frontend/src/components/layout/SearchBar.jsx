import { useTranslation } from 'react-i18next';
import { SearchIcon } from '../icons/Icons';
import styles from './SearchBar.module.css';

// حقل بحث عام (سطح المكتب داخل الشريط، والجوال داخل القائمة السفلية).
// نموذج حقيقي لا حقل عائم: Enter يُرسل، والقارئ الصوتي يعلن "بحث" بدوره الدلالي.
export default function SearchBar({ value, onChange, onSubmit, className = '' }) {
  const { t } = useTranslation();
  return (
    <form
      role="search"
      className={`${styles.wrap} ${className}`}
      onSubmit={(e) => { e.preventDefault(); onSubmit?.(); }}
    >
      <SearchIcon size={16} />
      <input
        type="search"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={t('nav.searchPlaceholder')}
        aria-label={t('nav.searchAria')}
      />
    </form>
  );
}
