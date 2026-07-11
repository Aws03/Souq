import { SearchIcon } from '../icons/Icons';
import styles from './SearchBar.module.css';

// حقل بحث عام (سطح المكتب داخل الشريط، والجوال داخل القائمة السفلية).
export default function SearchBar({ value, onChange, className = '' }) {
  return (
    <label className={`${styles.wrap} ${className}`}>
      <SearchIcon size={16} />
      <input
        type="search"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="ابحث عن منتج…"
        aria-label="ابحث عن منتج"
      />
    </label>
  );
}
